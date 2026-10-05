
namespace AgentLoop.StateMachines;


/// <summary>Una transicion consumada, para trazas y depuracion.</summary>
public readonly record struct StateTransition<TState>(
    TState From,
    TState To,
    DateTimeOffset At,
    string? Note)
    where TState : struct, Enum
{
    public override string ToString()
        => Note is null ? $"{From} -> {To}" : $"{From} -> {To}  ({Note})";
}
