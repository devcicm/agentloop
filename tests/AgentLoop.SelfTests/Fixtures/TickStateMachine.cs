
namespace AgentLoop.StateMachines;


/// <summary>
/// Subclase minima de <see cref="StateMachine{TState,TContext}"/> que nunca termina sola:
/// alterna Tick/Tock indefinidamente. Sirve para probar, sin red, que el corte del loop
/// depende solo de la bandera de la CAPA 4.
/// </summary>
public sealed class TickStateMachine : StateMachine<TickState, TickCounter>
{
    public TickStateMachine() : base(TickState.Tick)
    {
    }

    protected override TickState FailureState => TickState.Stopped;

    protected override bool IsTerminal(TickState state) => state == TickState.Stopped;

    protected override ValueTask<TickState> OnExecuteAsync(TickCounter context, CancellationToken cancellationToken)
    {
        context.Ticks++;
        return ValueTask.FromResult(CurrentState == TickState.Tick ? TickState.Tock : TickState.Tick);
    }
}
