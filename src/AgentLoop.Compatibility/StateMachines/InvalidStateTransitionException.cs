
namespace AgentLoop.StateMachines;


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
