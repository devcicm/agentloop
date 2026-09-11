using System.Collections.Concurrent;
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

    public AgentEvent Publish(AgentEventKind kind, string source, string text, object? data = null)
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
            payload);

        if (IsDuplicate(item))
        {
            Interlocked.Increment(ref _discardedByDedup);
            OnDuplicateDiscarded?.Invoke(item);
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

        OnPublished?.Invoke(item);
        _signal.Set();
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
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => PendingCount > 0 ? Task.FromResult(true) : _signal.WaitAsync(timeout, cancellationToken);

    public IReadOnlyList<AgentEvent> Recent(int count)
    {
        lock (_historyGate)
        {
            return _history.TakeLast(count).ToArray();
        }
    }
}
