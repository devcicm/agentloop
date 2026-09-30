using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AgentLoop.Concurrency;

namespace AgentLoop.Events;

/// <summary>
/// Cola de sucesos con senal. Cualquiera publica desde cualquier hilo (handlers de salida
/// de procesos, temporizadores, herramientas) y el agente se entera esperando la senal.
///
/// La senal es un reset event automatico y no un contador: quien espera no gira en vacio
/// ni bloquea un hilo, y una publicacion no se pierde aunque llegue justo cuando nadie
/// estaba esperando todavia.
/// </summary>
public sealed class EventBus
{
    private readonly ConcurrentQueue<AgentEvent> _pending = new();
    private readonly AsyncAutoResetEvent _signal = new();
    private readonly object _historyGate = new();
    private readonly List<AgentEvent> _history = [];
    private readonly Dictionary<string, DateTimeOffset> _recentHashes = new(StringComparer.Ordinal);
    private readonly object _dedupGate = new();
    private long _sequence;
    private readonly object _publicationGate = new();
    private readonly List<Action<AgentEvent>> _subscribers = [];
    public Guid RunId { get; init; }
    public bool AsyncObservers { get; init; }
    private readonly object _observerGate = new();
    private Task _observerMailbox = Task.CompletedTask;

    public IDisposable Subscribe(Action<AgentEvent> subscriber)
    {
        lock (_publicationGate) _subscribers.Add(subscriber);
        return new Subscription(this, subscriber);
    }

    private sealed class Subscription(EventBus bus, Action<AgentEvent> handler) : IDisposable
    {
        public void Dispose() { lock (bus._publicationGate) bus._subscribers.Remove(handler); }
    }

    /// <summary>Tope del historial en memoria; lo viejo se descarta.</summary>
    public int MaxHistory { get; init; } = 500;

    /// <summary>
    /// Ventana de deduplicacion en segundos: si llega un AgentEvent con el mismo
    /// hash dentro de esta ventana, se descarta. 0 desactiva la deduplicacion.
    /// Default 2s. Cubre el caso del reactor que re-publica el mismo Aviso cada
    /// vez que el modelo narra vacio, evitando que el bus se retroalimente.
    /// </summary>
    public TimeSpan DedupWindow { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Traza en vivo de cada publicacion (la consola del CLI se engancha aqui).</summary>
    public Action<AgentEvent>? OnPublished { get; set; }

    /// <summary>Llamado cuando un evento se descarta por deduplicacion. Util para observabilidad.</summary>
    public Action<AgentEvent>? OnDuplicateDiscarded { get; set; }

    public int PendingCount => _pending.Count;

    public long Published => Interlocked.Read(ref _sequence);

    /// <summary>Cantidad de eventos descartados por deduplicacion desde el arranque.</summary>
    private long _discardedByDedup;

    public long DiscardedByDedup => Interlocked.Read(ref _discardedByDedup);

    private long _observerFailures;
    public long ObserverFailures => Interlocked.Read(ref _observerFailures);

    private void Notify(Action<AgentEvent>? observers, AgentEvent item)
    {
        if (observers is null) return;
        if (AsyncObservers)
        {
            lock (_observerGate)
                _observerMailbox = _observerMailbox.ContinueWith(_ => NotifyInline(observers, item),
                    CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return;
        }
        NotifyInline(observers, item);
    }

    private void NotifyInline(Action<AgentEvent> observers, AgentEvent item)
    {
        foreach (Action<AgentEvent> observer in observers.GetInvocationList())
        {
            try { observer(item); }
            catch { Interlocked.Increment(ref _observerFailures); }
        }
    }

    public IReadOnlyList<AgentEvent> History
    {
        get
        {
            lock (_historyGate)
            {
                return _history.ToArray();
            }
        }
    }

    public AgentEvent Publish(AgentEventKind kind, string source, string text, object? data = null, Guid? causationId = null)
    {
        lock (_publicationGate) return PublishOrdered(kind, source, text, data, causationId);
    }

    private AgentEvent PublishOrdered(AgentEventKind kind, string source, string text, object? data, Guid? causationId)
    {
        var payload = data switch
        {
            null => null,
            string s => s,
            _ => JsonSerializer.Serialize(data),
        };

        var item = new AgentEvent(
            Interlocked.Increment(ref _sequence),
            DateTimeOffset.Now,
            kind,
            source,
            text,
            payload) { RunId = RunId, CausationId = causationId };

        if (IsDuplicate(item))
        {
            Interlocked.Increment(ref _discardedByDedup);
            Notify(OnDuplicateDiscarded, item);
            // Devolvemos un AgentEvent "fantasma" con sequence 0 para que el
            // caller pueda comprobar Published (que ya incremento). No se
            // encola ni se anade al historial: el bus lo ignora.
            return item with { Sequence = 0 };
        }

        _pending.Enqueue(item);

        lock (_historyGate)
        {
            _history.Add(item);
            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(0, _history.Count - MaxHistory);
            }
        }

        _signal.Set();
        foreach (var subscriber in _subscribers.ToArray()) subscriber(item);
        Notify(OnPublished, item);
        return item;
    }

    /// <summary>
    /// Hash estable del contenido del evento: Kind + Source + Text + (Data si existe).
    /// Dos eventos con el mismo hash representan el mismo "hecho"; la
    /// deduplicacion los colapsa en la ventana configurable.
    /// </summary>
    private bool IsDuplicate(AgentEvent item)
    {
        if (DedupWindow <= TimeSpan.Zero)
        {
            return false;
        }

        var hash = $"{item.Kind}|{item.Source}|{item.Text}|{item.Data ?? string.Empty}";
        var now = DateTimeOffset.UtcNow;

        lock (_dedupGate)
        {
            // Limpia hashes viejos fuera de la ventana.
            var cutoff = now - DedupWindow;
            var expired = _recentHashes.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToArray();
            foreach (var key in expired)
            {
                _recentHashes.Remove(key);
            }

            if (_recentHashes.TryGetValue(hash, out var lastSeen) && lastSeen >= cutoff)
            {
                return true;
            }

            _recentHashes[hash] = now;
            return false;
        }
    }

    /// <summary>Se lleva lo que haya sin esperar. Nunca bloquea.</summary>
    public IReadOnlyList<AgentEvent> Drain(int max = 64)
    {
        var batch = new List<AgentEvent>();
        while (batch.Count < max && _pending.TryDequeue(out var item))
        {
            batch.Add(item);
        }

        return batch;
    }

    /// <summary>Espera a que entre algo. false = vencio el plazo sin novedades.</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (timeout < TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        var clock = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_pending.IsEmpty) return true;
            var remaining = timeout == Timeout.InfiniteTimeSpan ? timeout : timeout - clock.Elapsed;
            if (remaining <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan) return false;
            if (!await _signal.WaitAsync(remaining, cancellationToken).ConfigureAwait(false))
                return !_pending.IsEmpty;
            // Drain puede haber consumido el evento que genero la senal.
            // Solo una cola con datos autoriza reaccionar; se conserva el plazo original.
        }
    }

    public IReadOnlyList<AgentEvent> Recent(int count)
    {
        lock (_historyGate)
        {
            return _history.TakeLast(count).ToArray();
        }
    }
}
