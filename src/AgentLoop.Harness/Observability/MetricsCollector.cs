using System.Collections.Concurrent;

namespace AgentLoop.Harness.Observability;


/// <summary>
/// Contadores monotonos del Run. No son snapshots: solo incrementan, y se
/// serializan al final como parte del Journey. Asi el selftest puede
/// afirmar "el reactor hizo 5 ciclos, 2 tools, 1 retry transitorio" sin
/// parsear la traza textual.
///
/// Las metricas usan nombres en minuscula con puntos: "cycles", "tools",
/// "retries.transient", "processes.started", etc. El observer decide como
/// etiquetarlas (Prometheus prefiere "_"; aqui usamos "." por consistencia
/// con la consola del CLI).
/// </summary>
public sealed class MetricsCollector
{
    private readonly ConcurrentDictionary<string, long> _counters = new(StringComparer.Ordinal);

    /// <summary>Incrementa un contador en uno. Thread-safe.</summary>
    public long Increment(string name, long delta = 1, IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = tags is null || tags.Count == 0 ? name : TagKey(name, tags);
        return _counters.AddOrUpdate(key, delta, (_, current) => current + delta);
    }

    /// <summary>Lee el valor actual de un contador; 0 si no se ha incrementado nunca.</summary>
    public long Get(string name, IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = tags is null || tags.Count == 0 ? name : TagKey(name, tags);
        return _counters.TryGetValue(key, out var v) ? v : 0;
    }

    /// <summary>Snapshot inmutable de todos los contadores.</summary>
    public IReadOnlyDictionary<string, long> Snapshot()
        => new Dictionary<string, long>(_counters, StringComparer.Ordinal);

    public void Reset() => _counters.Clear();

    private static string TagKey(string name, IReadOnlyDictionary<string, string> tags)
    {
        var pairs = tags.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var rendered = string.Join(",", pairs.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{name}{{{rendered}}}";
    }
}
