using System.Text;
using System.Text.Json;
namespace AgentLoop.Harness.Core;


/// <summary>
/// Narrativa legible de un Run: Run + Steps en orden + resumen estructurado
/// + un escritor que produce markdown y json para inspeccion post-mortem.
/// El Journey se construye al cerrar el Run y se entrega al console observer,
/// al RunStore y a cualquier sink que quiera un resumen estable.
/// </summary>
public sealed record Journey(
    Guid RunId,
    string Objective,
    RunStatus FinalStatus,
    string Outcome,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    TimeSpan Elapsed,
    IReadOnlyList<Step> Steps,
    IReadOnlyDictionary<string, long> Metrics,
    string? FailureReason = null,
    EvidenceSnapshot? Evidence = null)
{
    public int ModelCalls { get; } = Steps.Count(s => s is ModelStep);
    public int ToolCalls { get; } = Steps.Count(s => s is ToolStep);
    public int Waits { get; } = Steps.Count(s => s is WaitStep);
    public int Errors { get; } = Steps.Count(s => s is ErrorStep);

    public override string ToString()
        => $"journey {RunId:N} status={FinalStatus} steps={Steps.Count} (model={ModelCalls} tool={ToolCalls} wait={Waits} err={Errors}) elapsed={Elapsed.TotalSeconds:0}s";
}
