using AgentLoop.StateMachines;

namespace AgentLoop.Looping;

/// <summary>Por que se levanto la bandera que rompio el loop.</summary>
public enum LoopBreakReason
{
    /// <summary>El loop sigue corriendo. Nunca aparece en un resultado.</summary>
    None = 0,

    /// <summary>El predicado BreakWhen devolvio true.</summary>
    ConditionMet,

    /// <summary>La maquina de estados llego a un estado terminal.</summary>
    TerminalState,

    /// <summary>Se agoto el presupuesto de iteraciones.</summary>
    MaxIterations,

    /// <summary>Alguien llamo a RequestBreak() desde fuera.</summary>
    Requested,

    /// <summary>Se cancelo el CancellationToken.</summary>
    Cancelled,

    /// <summary>Excepcion no controlada durante el paso.</summary>
    Faulted,
}

/// <summary>Foto de una iteracion, que es lo que recibe el predicado de corte.</summary>
public sealed record LoopContext<TState, TContext>(
    int Iteration,
    TState State,
    TContext Context,
    StateTransition<TState>? LastTransition)
    where TState : struct, Enum;

/// <summary>Motivo de fallo de un loop: termino con error o por una condicion no satisfecha.</summary>
public enum LoopFailureReason
{
    Errored,
    ConditionNotMet,
    Cancelled,
    Faulted,
}

/// <summary>Outcome de un loop exitoso: por que se rompio la bandera y cuantas iteraciones llevo.</summary>
public sealed record LoopOutcome<TState>(LoopBreakReason Reason, int Iterations, TState FinalState) where TState : struct, Enum;

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
