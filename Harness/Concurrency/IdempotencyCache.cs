using System.Collections.Concurrent;

namespace AgentLoop.Harness.Concurrency;

/// <summary>
/// Cache bounded para resultados de tools idempotentes. La primera llamada
/// ejecuta el factory y cachea; las siguientes con la misma clave devuelven
/// el resultado cacheado sin reejecutar.
///
/// El tope es LRU: cuando se llega al maximo, la entrada mas vieja se
/// descarta. Asi el cache no crece sin freno si el modelo genera miles
/// de claves distintas por descuido.
///
/// El cache vive dentro de un Run: se construye en GoalRunFactory y se
/// invalida (vía Clear) cuando el Run termina. Asi nunca se sirven
/// resultados de un Run anterior.
///
/// Concurrencia: internamente cada entrada es un <see cref="Lazy{T}"/> con
/// <c>ExecutionAndPublication</c>, asi si N hilos llaman GetOrAdd con la
/// misma clave al mismo tiempo solo uno ejecuta el factory; los demas
/// esperan al Lazy y obtienen el mismo valor.
/// </summary>
public sealed class IdempotencyCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskEntry> _taskEntries = new(StringComparer.Ordinal);
    private readonly int _capacity;

    public IdempotencyCache(int capacity = 256)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Debe ser >= 1.");
        }

        _capacity = capacity;
    }

    public int Count => _entries.Count;

    public int Capacity => _capacity;

    /// <summary>Cuenta total de entradas (sync + task).</summary>
    public int GetCount() => _entries.Count + _taskEntries.Count;

    private long _accessCounter;

    /// <summary>
    /// Devuelve el resultado cacheado para <paramref name="key"/>, ejecutando
    /// <paramref name="factory"/> la primera vez (potencialmente desde varios
    /// hilos a la vez, pero solo uno ejecuta el factory gracias al Lazy).
    /// </summary>
    public string GetOrAdd(string key, Func<string> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        // Capacidad: si esta lleno y la clave es nueva, evictamos el mas viejo.
        if (_entries.Count >= _capacity && !_entries.ContainsKey(key))
        {
            EvictOldest();
        }

        var lazy = _entries.GetOrAdd(key, _ => new Entry(
            new Lazy<string>(factory, LazyThreadSafetyMode.ExecutionAndPublication),
            Interlocked.Increment(ref _accessCounter)));

        // Refresca LRU en hit: el Lazy ya esta calculado o calculandose.
        var current = lazy.WithAccess(Interlocked.Increment(ref _accessCounter));
        _entries[key] = current;
        return current.Value.Value;
    }

    /// <summary>
    /// Variante asincrona: devuelve un Task que ejecuta el factory la primera
    /// vez y devuelve el resultado cacheado en hits. Garantiza singleflight:
    /// si N hilos llaman con la misma clave, el factory corre exactamente 1
    /// vez (los demas esperan al Task resultado).
    /// </summary>
    public Task<string> GetOrAddTaskAsync(string key, Func<Task<string>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (_entries.Count >= _capacity && !_taskEntries.ContainsKey(key))
        {
            EvictOldestTask();
        }

        var lazy = _taskEntries.GetOrAdd(key, _ => new TaskEntry(
            new Lazy<Task<string>>(factory, LazyThreadSafetyMode.ExecutionAndPublication),
            Interlocked.Increment(ref _accessCounter)));

        var current = lazy.WithAccess(Interlocked.Increment(ref _accessCounter));
        _taskEntries[key] = current;
        return current.Value.Value;
    }

    public bool TryGet(string key, out string value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            entry = entry.WithAccess(Interlocked.Increment(ref _accessCounter));
            _entries[key] = entry;
            value = entry.Value.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public void Set(string key, string value)
    {
        if (_entries.Count >= _capacity && !_entries.ContainsKey(key))
        {
            EvictOldest();
        }

        _entries[key] = new Entry(
            new Lazy<string>(() => value, LazyThreadSafetyMode.PublicationOnly),
            Interlocked.Increment(ref _accessCounter));
    }

    public void Clear() => _entries.Clear();

    private void EvictOldest()
    {
        string? oldestKey = null;
        long oldestTicks = long.MaxValue;

        foreach (var (key, entry) in _entries)
        {
            if (entry.LastAccessTicks < oldestTicks)
            {
                oldestTicks = entry.LastAccessTicks;
                oldestKey = key;
            }
        }

        if (oldestKey is not null)
        {
            _entries.TryRemove(oldestKey, out _);
        }
    }

    private void EvictOldestTask()
    {
        string? oldestKey = null;
        long oldestTicks = long.MaxValue;

        foreach (var (key, entry) in _taskEntries)
        {
            if (entry.LastAccessTicks < oldestTicks)
            {
                oldestTicks = entry.LastAccessTicks;
                oldestKey = key;
            }
        }

        if (oldestKey is not null)
        {
            _taskEntries.TryRemove(oldestKey, out _);
        }
    }

    private sealed record Entry(Lazy<string> Value, long LastAccessTicks)
    {
        public Entry WithAccess(long ticks) => new(Value, ticks);
    }

    private sealed record TaskEntry(Lazy<Task<string>> Value, long LastAccessTicks)
    {
        public TaskEntry WithAccess(long ticks) => new(Value, ticks);
    }
}
