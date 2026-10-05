
namespace AgentLoop.Discovery;


/// <summary>Reporte de capacidades de un modelo.</summary>
public sealed class ModelCapabilityReport
{
    public ModelCapabilityReport(string modelId)
    {
        ModelId = modelId;
    }

    public string ModelId { get; }

    public bool Reachable { get; set; }

    /// <summary>Modelo real que atendio la peticion detras del gateway.</summary>
    public string BackendModel { get; set; } = string.Empty;

    public Dictionary<string, CapabilityProbe> Capabilities { get; } = [];

    /// <summary>Motivo por el que se descarto, cuando no es agentico.</summary>
    public string? SkipReason { get; set; }

    public bool IsAgentic => CapabilityNames.RequiredForAgentic.All(Supports);

    public bool Supports(string capability)
        => Capabilities.TryGetValue(capability, out var probe) && probe.Supported;

    public IReadOnlyList<string> SupportedNames
        => CapabilityNames.All.Where(Supports).ToList();

    public void Record(CapabilityProbe probe) => Capabilities[probe.Name] = probe;

    public object ToSerializable() => new
    {
        model_id = ModelId,
        reachable = Reachable,
        backend_model = BackendModel,
        is_agentic = IsAgentic,
        supported = SupportedNames,
        skip_reason = SkipReason,
        capabilities = Capabilities.ToDictionary(
            kv => kv.Key,
            kv => (object)new
            {
                supported = kv.Value.Supported,
                evidence = kv.Value.Evidence,
                latency_ms = Math.Round(kv.Value.LatencyMs, 1),
                error = kv.Value.Error,
            }),
    };
}
