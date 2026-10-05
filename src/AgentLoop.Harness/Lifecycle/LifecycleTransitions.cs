namespace AgentLoop.Harness.Lifecycle;


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
