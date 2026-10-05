
namespace AgentLoop.Discovery;


/// <summary>Resultado de una sonda individual.</summary>
public sealed record CapabilityProbe(
    string Name,
    bool Supported,
    string Evidence,
    double LatencyMs,
    string? Error);
