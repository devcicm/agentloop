using AgentLoop.Goals;

namespace AgentLoop.Harness.Core;

/// <summary>
/// Estado vivo de una operacion dirigida por eventos, en el nivel del Run.
/// Escribible desde el reactor; se reconstruye desde un Snapshot para
/// reanudar; se serializa como JSON para persistir.
///
/// Por ahora delega en GoalContext para el estado del modelo y del ledger;
/// cuando el refactor del commit H cierre, GoalContext pasara a vivir
/// dentro de RunContext y este tipo sera la unica raiz de estado.
/// </summary>
public sealed class RunContext
{
    public required Guid RunId { get; init; }

    public required GoalContext Goal { get; init; }

    public required Traceability.CorrelationContext Correlation { get; init; }

    public required Scope Scope { get; init; }

    public Steps.StepRecorder Steps { get; init; } = null!;

    public Observability.MetricsCollector Metrics { get; init; } = null!;

    public RunStatus Status { get; set; } = RunStatus.Created;

    public string? FailureReason { get; set; }

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    public DateTimeOffset? EndedAt { get; set; }

    public TimeSpan Elapsed => (EndedAt ?? DateTimeOffset.Now) - StartedAt;
}
