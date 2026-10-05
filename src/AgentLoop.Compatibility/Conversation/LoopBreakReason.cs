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
