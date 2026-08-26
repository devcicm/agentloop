using AgentLoop.StateMachines;

namespace AgentLoop.Looping;

/// <summary>
/// CAPA 4 - el loop del harness.
///
/// El bucle no tiene condicion propia: gira mientras <see cref="IsBroken"/> sea false.
/// Cada iteracion avanza un paso de la maquina de estados y luego evalua las condiciones
/// de corte; la primera que se cumple pone la bandera en true y anota el motivo en
/// <see cref="BreakReason"/>, con lo que el while termina.
///
/// Es generico a proposito: sirve para cualquier StateMachine, no solo para la del agente.
/// </summary>
public sealed class AgentLoopRunner<TState, TContext>
    where TState : struct, Enum
{
    private readonly StateMachine<TState, TContext> _machine;

    public AgentLoopRunner(StateMachine<TState, TContext> machine)
    {
        _machine = machine;
    }

    /// <summary>Bandera de corte. Mientras sea false el loop sigue girando.</summary>
    public bool IsBroken { get; private set; }

    public LoopBreakReason BreakReason { get; private set; } = LoopBreakReason.None;

    public int Iteration { get; private set; }

    /// <summary>Tope duro de pasos. Evita que un modelo que insiste en llamar tools gire para siempre.</summary>
    public int MaxIterations { get; init; } = 12;

    /// <summary>
    /// Condicion de exito del dominio. Si devuelve true, el loop se rompe con
    /// <see cref="LoopBreakReason.ConditionMet"/>.
    /// </summary>
    public Func<LoopContext<TState, TContext>, bool>? BreakWhen { get; init; }

    /// <summary>Observador por iteracion (trazas, metricas). No influye en el corte.</summary>
    public Action<LoopContext<TState, TContext>>? OnIteration { get; init; }

    public StateMachine<TState, TContext> Machine => _machine;

    /// <summary>Rompe el loop desde fuera; surte efecto al terminar la iteracion en curso.</summary>
    public void RequestBreak() => Break(LoopBreakReason.Requested);

    public void Reset()
    {
        IsBroken = false;
        BreakReason = LoopBreakReason.None;
        Iteration = 0;
    }

    public async Task<LoopResult<TState, TContext>> RunAsync(TContext context, CancellationToken cancellationToken = default)
    {
        Reset();
        Exception? error = null;

        while (!IsBroken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Break(LoopBreakReason.Cancelled);
                break;
            }

            Iteration++;

            TState state;
            try
            {
                state = await _machine.StepAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Break(LoopBreakReason.Cancelled);
                break;
            }
            catch (Exception ex)
            {
                // La maquina ya absorbe los errores de dominio en su estado de fallo;
                // aqui solo caen problemas estructurales (transicion invalida, bug interno).
                error = ex;
                Break(LoopBreakReason.Faulted);
                break;
            }

            var iteration = new LoopContext<TState, TContext>(
                Iteration,
                state,
                context,
                _machine.History.Count > 0 ? _machine.History[^1] : null);

            OnIteration?.Invoke(iteration);
            Evaluate(iteration, cancellationToken);
        }

        return new LoopResult<TState, TContext>(
            IsBroken,
            BreakReason,
            Iteration,
            _machine.CurrentState,
            context,
            error ?? _machine.LastError,
            _machine.History);
    }

    /// <summary>Orden de prioridad de las condiciones de corte.</summary>
    private void Evaluate(LoopContext<TState, TContext> iteration, CancellationToken cancellationToken)
    {
        if (BreakWhen is not null && BreakWhen(iteration))
        {
            Break(LoopBreakReason.ConditionMet);
            return;
        }

        if (_machine.IsFinished)
        {
            Break(LoopBreakReason.TerminalState);
            return;
        }

        if (Iteration >= MaxIterations)
        {
            Break(LoopBreakReason.MaxIterations);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            Break(LoopBreakReason.Cancelled);
        }
    }

    private void Break(LoopBreakReason reason)
    {
        IsBroken = true;
        BreakReason = reason;
    }
}
