
namespace AgentLoop.StateMachines;


/// <summary>
/// CAPA 3 - maquina de estados generica pensada para heredarse.
///
/// Una subclase solo esta obligada a decir tres cosas: que hace en cada estado
/// (<see cref="OnExecuteAsync"/>), cuales son terminales (<see cref="IsTerminal"/>)
/// y a donde va cuando algo explota (<see cref="FailureState"/>). Todo lo demas
/// (<see cref="CanTransition"/>, <see cref="OnEnterAsync"/>, <see cref="OnExitAsync"/>,
/// <see cref="OnErrorAsync"/>) son puntos de extension opcionales.
///
/// No contiene bucle: avanza de a un paso con <see cref="StepAsync"/>. Quien itera
/// es la CAPA 4 (AgentLoopRunner), para que el criterio de corte viva fuera.
/// </summary>
public abstract class StateMachine<TState, TContext>
    where TState : struct, Enum
{
    private readonly List<StateTransition<TState>> _history = [];

    protected StateMachine(TState initialState)
    {
        InitialState = initialState;
        CurrentState = initialState;
    }

    public TState InitialState { get; }

    public TState CurrentState { get; private set; }

    public IReadOnlyList<StateTransition<TState>> History => _history;

    /// <summary>Ultima excepcion capturada por <see cref="StepAsync"/>, si la hubo.</summary>
    public Exception? LastError { get; protected set; }

    public bool IsFinished => IsTerminal(CurrentState);

    /// <summary>Nota que la subclase puede fijar para explicar la proxima transicion en la traza.</summary>
    protected string? PendingNote { get; set; }

    /// <summary>Ejecuta el estado actual y devuelve el siguiente. Unico metodo con logica obligatoria.</summary>
    protected abstract ValueTask<TState> OnExecuteAsync(TContext context, CancellationToken cancellationToken);

    protected abstract bool IsTerminal(TState state);

    /// <summary>Estado al que cae la maquina cuando <see cref="OnExecuteAsync"/> lanza.</summary>
    protected abstract TState FailureState { get; }

    protected virtual bool CanTransition(TState from, TState to) => true;

    protected virtual ValueTask OnEnterAsync(TState state, TContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    protected virtual ValueTask OnExitAsync(TState state, TContext context, CancellationToken cancellationToken)
        => ValueTask.CompletedTask;

    /// <summary>
    /// Traduce una excepcion en un estado. El default va a <see cref="FailureState"/>;
    /// una subclase puede sobreescribirlo para, por ejemplo, reintentar ante errores transitorios.
    /// </summary>
    protected virtual ValueTask<TState> OnErrorAsync(Exception error, TContext context, CancellationToken cancellationToken)
    {
        PendingNote = $"{error.GetType().Name}: {error.Message}";
        return ValueTask.FromResult(FailureState);
    }

    /// <summary>
    /// Avanza exactamente una transicion. Si ya esta en estado terminal no hace nada,
    /// asi que llamarlo de mas es inofensivo.
    /// </summary>
    public async ValueTask<TState> StepAsync(TContext context, CancellationToken cancellationToken = default)
    {
        if (IsFinished)
        {
            return CurrentState;
        }

        cancellationToken.ThrowIfCancellationRequested();

        TState next;
        try
        {
            next = await OnExecuteAsync(context, cancellationToken).ConfigureAwait(false);
            // La ejecucion salio bien: si habia un error de un paso anterior
            // (p. ej. un 503 transitorio ya reintentado), deja de ser el error
            // vigente. Sin esto, LastError se queda pegado y el resumen final
            // muestra un fallo del que la operacion ya se recupero.
            LastError = null;
        }
        catch (OperationCanceledException)
        {
            throw; // la cancelacion la gestiona quien itera, no es un fallo de la maquina
        }
        catch (Exception error)
        {
            LastError = error;
            next = await OnErrorAsync(error, context, cancellationToken).ConfigureAwait(false);
        }

        return await TransitionToAsync(next, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Fuerza una transicion respetando los hooks. Util en tests y en subclases.</summary>
    protected async ValueTask<TState> TransitionToAsync(TState next, TContext context, CancellationToken cancellationToken)
    {
        var from = CurrentState;
        if (!CanTransition(from, next))
        {
            throw new InvalidStateTransitionException(GetType().Name, from, next);
        }

        await OnExitAsync(from, context, cancellationToken).ConfigureAwait(false);

        CurrentState = next;
        _history.Add(new StateTransition<TState>(from, next, DateTimeOffset.UtcNow, PendingNote));
        PendingNote = null;

        await OnEnterAsync(next, context, cancellationToken).ConfigureAwait(false);
        return next;
    }

    /// <summary>Devuelve la maquina al estado inicial (o al indicado) y limpia la traza.</summary>
    public void Reset(TState? state = null)
    {
        CurrentState = state ?? InitialState;
        LastError = null;
        PendingNote = null;
        _history.Clear();
    }

    public string DescribeHistory()
        => _history.Count == 0
            ? $"(sin transiciones, estado {CurrentState})"
            : string.Join(Environment.NewLine, _history.Select((t, i) => $"  {i + 1,2}. {t}"));
}
