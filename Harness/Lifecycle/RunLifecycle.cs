using AgentLoop.Harness.Core;

namespace AgentLoop.Harness.Lifecycle;

/// <summary>
/// Estados por los que pasa un Run durante su vida. Es la version explicita
/// de los RunStatus que el reactor ya emite implicitamente: cuando duerme
/// sobre el bus sin procesos vivos, esta en Paused; cuando se reactiva por
/// una senal, transiciona a Running. Hoy eso se perdia; con lifecycle
/// queda trazado en el Journey.
/// </summary>
public enum RunLifecycleState
{
    Created,
    Pending,
    Running,
    Paused,
    Suspending,
    Resuming,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    Checkpointed,
    ResumedFromCheckpoint,
}

/// <summary>
/// Tabla de transiciones validas. Una transicion no listada aqui se
/// rechaza en <see cref="IsValid"/>; asi no se publican cambios de estado
/// incoherentes (ej. Running -> ResumedFromCheckpoint sin pasar por
/// Suspending).
/// </summary>
public static class LifecycleTransitions
{
    private static readonly Dictionary<RunLifecycleState, RunLifecycleState[]> Allowed = new()
    {
        [RunLifecycleState.Created] = [RunLifecycleState.Pending, RunLifecycleState.Cancelled],
        [RunLifecycleState.Pending] = [RunLifecycleState.Running, RunLifecycleState.Cancelled],
        [RunLifecycleState.Running] = [RunLifecycleState.Paused, RunLifecycleState.Suspending, RunLifecycleState.Succeeded, RunLifecycleState.Failed, RunLifecycleState.Cancelled, RunLifecycleState.TimedOut, RunLifecycleState.Checkpointed],
        [RunLifecycleState.Paused] = [RunLifecycleState.Running, RunLifecycleState.Suspending, RunLifecycleState.Failed, RunLifecycleState.TimedOut, RunLifecycleState.Cancelled, RunLifecycleState.Checkpointed],
        [RunLifecycleState.Suspending] = [RunLifecycleState.Paused, RunLifecycleState.Running, RunLifecycleState.Cancelled],
        [RunLifecycleState.Resuming] = [RunLifecycleState.Running, RunLifecycleState.Cancelled, RunLifecycleState.Failed],
        [RunLifecycleState.Checkpointed] = [RunLifecycleState.Running, RunLifecycleState.Paused, RunLifecycleState.Cancelled],
        [RunLifecycleState.ResumedFromCheckpoint] = [RunLifecycleState.Running],
        [RunLifecycleState.Succeeded] = [],
        [RunLifecycleState.Failed] = [],
        [RunLifecycleState.Cancelled] = [],
        [RunLifecycleState.TimedOut] = [],
    };

    public static bool IsValid(RunLifecycleState from, RunLifecycleState to)
        => Allowed.TryGetValue(from, out var allowed) && allowed.Contains(to);

    public static IReadOnlyList<RunLifecycleState> AllowedFrom(RunLifecycleState from)
        => Allowed.TryGetValue(from, out var allowed) ? allowed : [];
}

/// <summary>
/// Suceso publicado cuando un Run cambia de estado. Lleva el from/to, un
/// timestamp, un motivo legible y un payload opcional para datos
/// estructurados (ciclo en que se tomo la decision, contador al cambio,
/// etc.).
/// </summary>
public sealed record LifecycleEvent(
    Guid RunId,
    RunLifecycleState From,
    RunLifecycleState To,
    DateTimeOffset At,
    string? Reason,
    IReadOnlyDictionary<string, object?>? Payload = null)
{
    public override string ToString()
        => $"#{RunId:N} {At:HH:mm:ss.fff} {From} -> {To}{(string.IsNullOrEmpty(Reason) ? string.Empty : $" ({Reason})")}";
}
