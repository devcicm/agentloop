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

/// <summary>Se intento una transicion que <c>CanTransition</c> rechaza.</summary>
public sealed class InvalidStateTransitionException : InvalidOperationException
{
    public InvalidStateTransitionException(string machine, object from, object to)
        : base($"{machine}: transicion no permitida {from} -> {to}")
    {
        From = from;
        To = to;
    }

    public object From { get; }

    public object To { get; }
}
