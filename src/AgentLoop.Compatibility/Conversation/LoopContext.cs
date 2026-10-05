namespace AgentLoop.Looping;


/// <summary>Foto de una iteracion, que es lo que recibe el predicado de corte.</summary>
public sealed record LoopContext<TState, TContext>(
    int Iteration,
    TState State,
    TContext Context,
    StateTransition<TState>? LastTransition)
    where TState : struct, Enum;
