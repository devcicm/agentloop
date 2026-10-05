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
///
/// Caducidad (agentico: TTL al leer): cada entrada lleva su instante de
/// expiracion. Al leer, una entrada caducada se trata como miss: se
/// descarta y se recrea. Sin TTL (null) las entradas no caducan.
/// </summary>
public sealed class IdempotencyCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, TaskEntry> _taskEntries = new(StringComparer.Ordinal);
    private readonly int _capacity;
    private readonly TimeSpan? _ttl;

    public IdempotencyCache(int capacity = 256, TimeSpan? ttl = null)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Debe ser >= 1.");
        }

        if (ttl is not null && ttl.Value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(ttl), "Debe ser positivo o null (sin caducidad).");
        }

        _capacity = capacity;
        _ttl = ttl;
    }

    public int Count => _entries.Count;

    public int Capacity => _capacity;

    /// <summary>Cuenta total de entradas (sync + task).</summary>
    public int GetCount() => _entries.Count + _taskEntries.Count;

    private long _accessCounter;

    private long ExpiryForNew()
        => _ttl is null ? long.MaxValue : Environment.TickCount64 + (long)_ttl.Value.TotalMilliseconds;

    private static bool IsExpired(long expiresAtTicks, long nowTicks) => nowTicks >= expiresAtTicks;

    /// <summary>
    /// Devuelve el resultado cacheado para <paramref name="key"/>, ejecutando
    /// <paramref name="factory"/> la primera vez (potencialmente desde varios
    /// hilos a la vez, pero solo uno ejecuta el factory gracias al Lazy).
    /// Una entrada caducada cuenta como miss y se recrea.
    /// </summary>
    public string GetOrAdd(string key, Func<string> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            // Capacidad: si esta lleno y la clave es nueva, evictamos el mas viejo.
            if (_entries.Count >= _capacity && !_entries.ContainsKey(key))
            {
                EvictOldest();
            }

            var lazy = _entries.GetOrAdd(key, _ => new Entry(
                new Lazy<string>(factory, LazyThreadSafetyMode.ExecutionAndPublication),
                Interlocked.Increment(ref _accessCounter),
                ExpiryForNew()));

            // Refresca LRU en hit: el Lazy ya esta calculado o calculandose.
            var current = lazy.WithAccess(Interlocked.Increment(ref _accessCounter));
            _entries[key] = current;
            if (!IsExpired(current.ExpiresAtTicks, Environment.TickCount64))
            {
                return current.Value.Value;
            }

            _entries.TryRemove(new KeyValuePair<string, Entry>(key, current));
        }

        // Contencion extrema: ejecutar sin cachear antes que girar para siempre.
        return factory();
    }

    /// <summary>
    /// Variante asincrona: devuelve un Task que ejecuta el factory la primera
    /// vez y devuelve el resultado cacheado en hits. Garantiza singleflight:
    /// si N hilos llaman con la misma clave, el factory corre exactamente 1
    /// vez (los demas esperan al Task resultado). Una entrada caducada cuenta
    /// como miss y se recrea.
    /// </summary>
    public Task<string> GetOrAddTaskAsync(string key, Func<Task<string>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_taskEntries.Count >= _capacity && !_taskEntries.ContainsKey(key))
            {
                EvictOldestTask();
            }

            var lazy = _taskEntries.GetOrAdd(key, _ => new TaskEntry(
                new Lazy<Task<string>>(factory, LazyThreadSafetyMode.ExecutionAndPublication),
                Interlocked.Increment(ref _accessCounter),
                ExpiryForNew()));

            var current = lazy.WithAccess(Interlocked.Increment(ref _accessCounter));
            _taskEntries[key] = current;
            if (!IsExpired(current.ExpiresAtTicks, Environment.TickCount64))
            {
                return current.Value.Value;
            }

            _taskEntries.TryRemove(new KeyValuePair<string, TaskEntry>(key, current));
        }

        // Contencion extrema: ejecutar sin cachear antes que girar para siempre.
        return factory();
    }

    public bool TryGet(string key, out string value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            if (IsExpired(entry.ExpiresAtTicks, Environment.TickCount64))
            {
                _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
            }
            else
            {
                entry = entry.WithAccess(Interlocked.Increment(ref _accessCounter));
                _entries[key] = entry;
                value = entry.Value.Value;
                return true;
            }
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
            Interlocked.Increment(ref _accessCounter),
            ExpiryForNew());
    }

    /// <summary>
    /// Quita una clave de ambas tablas (sync y task). Idempotente: quitar lo
    /// que no existe devuelve false sin lanzar. Lo usa el release-on-failure:
    /// un efecto fallido no debe dejar la clave marcada.
    /// </summary>
    public bool Remove(string key)
        => _entries.TryRemove(key, out _) | _taskEntries.TryRemove(key, out _);

    public void Clear()
    {
        _entries.Clear();
        _taskEntries.Clear();
    }

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

    private sealed record Entry(Lazy<string> Value, long LastAccessTicks, long ExpiresAtTicks)
    {
        public Entry WithAccess(long ticks) => new(Value, ticks, ExpiresAtTicks);
    }

    private sealed record TaskEntry(Lazy<Task<string>> Value, long LastAccessTicks, long ExpiresAtTicks)
    {
        public TaskEntry WithAccess(long ticks) => new(Value, ticks, ExpiresAtTicks);
    }
}
