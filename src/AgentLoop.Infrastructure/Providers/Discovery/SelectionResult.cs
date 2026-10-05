using System.Diagnostics;
using System.Text.Json;
namespace AgentLoop.Discovery;


/// <summary>Salida del sondeo: los modelos agenticos encontrados y todo lo que se probo.</summary>
public sealed record SelectionResult(
    IReadOnlyList<ModelCapabilityReport> Agentic,
    IReadOnlyList<ModelCapabilityReport> AllProbed,
    int CandidatesConsidered,
    int Requested)
{
    public bool ReachedTarget => Agentic.Count >= Requested;
}
