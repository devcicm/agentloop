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
    public bool Succeeded => BreakReason is LoopBreakReason.ConditionMet or LoopBreakReason.TerminalState && Error is null;

    public override string ToString()
        => $"IsBroken={IsBroken} BreakReason={BreakReason} Iterations={Iterations} FinalState={FinalState}";
}
