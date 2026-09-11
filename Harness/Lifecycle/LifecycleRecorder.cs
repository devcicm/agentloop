using AgentLoop.Harness.Core;
using AgentLoop.Harness.Observability;
using AgentLoop.Harness.Steps;

namespace AgentLoop.Harness.Lifecycle;

/// <summary>
/// Contrato de un sink de transiciones. Implementarlo no obliga a tocar
/// el resto del harness: cualquier consumidor (consola, archivo,
/// observabilidad) lo implementa y se suscribe.
/// </summary>
public interface ILifecycleObserver
{
    void OnLifecycle(LifecycleEvent transition);
}

/// <summary>
/// Implementacion de referencia que graba cada transicion como Step en el
/// StepRecorder y la reenvia a los observers de Harness.Observability
/// como un log estructurado. Asi el Journey muestra las transiciones
/// como filas mas en la tabla de pasos.
/// </summary>
public sealed class LifecycleRecorder : ILifecycleObserver
{
    private readonly StepRecorder _recorder;
    private readonly IReadOnlyList<IRunObserver> _observers;

    public LifecycleRecorder(StepRecorder recorder, IReadOnlyList<IRunObserver> observers)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _observers = observers ?? [];
    }

    public void OnLifecycle(LifecycleEvent transition)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var step = new TransitionStep(
            _recorder.NextSequence(),
            transition.At,
            transition.From.ToString(),
            transition.To.ToString(),
            transition.Reason);

        _recorder.Record(step);

        foreach (var observer in _observers)
        {
            try
            {
                observer.OnLog(new StructuredLog(
                    transition.At,
                    transition.To is RunLifecycleState.Failed or RunLifecycleState.TimedOut ? LogLevel.Warning : LogLevel.Info,
                    "lifecycle",
                    transition.ToString()));
            }
            catch
            {
                // Un observer que falla no rompe la grabacion.
            }
        }
    }

    /// <summary>Atajo para emitir una transicion solo si es valida; si no, devuelve false.</summary>
    public bool TryTransition(RunLifecycleState from, RunLifecycleState to, string? reason = null, IReadOnlyDictionary<string, object?>? payload = null)
    {
        if (!LifecycleTransitions.IsValid(from, to))
        {
            return false;
        }

        OnLifecycle(new LifecycleEvent(_recorder.RunId, from, to, DateTimeOffset.Now, reason, payload));
        return true;
    }
}
