namespace AgentLoop.Looping;


/// <summary>Resultado final del loop: siempre dice por que se detuvo.</summary>
public sealed record LoopResult<TState, TContext>(
    bool IsBroken,
    LoopBreakReason BreakReason,
    int Iterations,
    TState FinalState,
    TContext Context,
    Exception? Error,
    IReadOnlyList<StateTransition<TState>> History)
    where TState : struct, Enum
{
    /// <summary>Vista como tipo suma. Ok cuando ConditionMet o TerminalState sin error.</summary>
    public Harness.Functional.Result<LoopOutcome<TState>, LoopFailureReason> Result
        => Error is not null
            ? Harness.Functional.Result<LoopOutcome<TState>, LoopFailureReason>.Fail(LoopFailureReason.Errored)
            : BreakReason switch
            {
                LoopBreakReason.ConditionMet or LoopBreakReason.TerminalState => Harness.Functional.Result<LoopOutcome<TState>, LoopFailureReason>.Ok(new LoopOutcome<TState>(BreakReason, Iterations, FinalState)),
                LoopBreakReason.Cancelled => Harness.Functional.Result<LoopOutcome<TState>, LoopFailureReason>.Fail(LoopFailureReason.Cancelled),
                LoopBreakReason.Faulted => Harness.Functional.Result<LoopOutcome<TState>, LoopFailureReason>.Fail(LoopFailureReason.Faulted),
                _ => Harness.Functional.Result<LoopOutcome<TState>, LoopFailureReason>.Fail(LoopFailureReason.ConditionNotMet),
            };

    public override string ToString()
        => $"IsBroken={IsBroken} BreakReason={BreakReason} Iterations={Iterations} FinalState={FinalState}";
}
