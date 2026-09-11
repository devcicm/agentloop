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
    private long _sequence;

    /// <summary>Tope del historial en memoria; lo viejo se descarta.</summary>
    public int MaxHistory { get; init; } = 500;

    /// <summary>Traza en vivo de cada publicacion (la consola del CLI se engancha aqui).</summary>
    public Action<AgentEvent>? OnPublished { get; init; }

    public int PendingCount => _pending.Count;

    public long Published => Interlocked.Read(ref _sequence);

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
