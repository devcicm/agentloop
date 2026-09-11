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
/// </summary>
public sealed class IdempotencyCache
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
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

    public bool TryGet(string key, out string value)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            // Hit: refresca el LRU creando una Entry nueva (records son
            // inmutables). El ConcurrentDictionary la reemplaza.
            _entries[key] = entry.WithAccess(Environment.TickCount64);
            value = entry.Value;
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

        _entries[key] = new Entry(value, Environment.TickCount64);
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

    private sealed record Entry(string Value, long LastAccessTicks)
    {
        public Entry WithAccess(long ticks) => new(Value, ticks);
    }
}
