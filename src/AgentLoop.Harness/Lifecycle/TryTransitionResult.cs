namespace AgentLoop.Harness.Lifecycle;


/// <summary>Resultado de TryTransitionAsync: si fue aceptada o por que se rechazo.</summary>
public readonly record struct TryTransitionResult(bool IsAccepted, RunLifecycleState From, RunLifecycleState To, string? Reason)
{
    public static TryTransitionResult Ok() => new(true, default, default, null);
    public static TryTransitionResult Rejected(RunLifecycleState from, RunLifecycleState to)
        => new(false, from, to, $"transicion invalida de {from} a {to}");
}
