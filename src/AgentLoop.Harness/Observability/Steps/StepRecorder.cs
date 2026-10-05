using System.Collections.Concurrent;
namespace AgentLoop.Harness.Steps;


/// <summary>
/// Graba los Steps en orden con un Sequence monotono y los publica a los
/// observers suscritos. Thread-safe: el reactor publica desde un solo
/// hilo pero el supervisor de procesos lo hace desde los hilos de captura.
///
/// Cada Step lleva su Correlation para que el Journey pueda correlacionar
/// con logs y metricas. Si nadie establecio Correlation, queda null y el
/// observer cae a un identificador vacio.
/// </summary>
public sealed class StepRecorder : IRunObserver
{
    private readonly ConcurrentQueue<Step> _steps = new();
    private long _sequence;
    private readonly object _observerGate = new();
    private Task _observerMailbox = Task.CompletedTask;
    public bool AsyncObservers { get; init; }

    public Guid RunId { get; }

    public IReadOnlyList<IRunObserver> Observers { get; }

    public StepRecorder(Guid runId, IReadOnlyList<IRunObserver> observers)
    {
        RunId = runId;
        Observers = observers ?? [];
    }

    public IReadOnlyList<Step> Steps => _steps.ToArray();

    public int Count => _steps.Count;

    public void Record(Step step)
    {
        ArgumentNullException.ThrowIfNull(step);
        _steps.Enqueue(step);
        var envelope = new StepEnvelope(
            RunId,
            step.Sequence,
            step.At,
            step.Kind.ToString(),
            Correlation.Current,
            step.Data);

        void Notify()
        {
            foreach (var observer in Observers)
            {
                try { observer.OnStep(envelope); } catch { }
            }
        }
        if (!AsyncObservers) Notify();
        else lock (_observerGate)
            _observerMailbox = _observerMailbox.ContinueWith(_ => Notify(), TaskScheduler.Default);
    }

    public long NextSequence() => Interlocked.Increment(ref _sequence);

    public void OnRunStart(Guid runId)
    {
        foreach (var observer in Observers)
        {
            try { observer.OnRunStart(runId); } catch { }
        }
    }

    public void OnRunEnd(Guid runId, string outcome)
    {
        foreach (var observer in Observers)
        {
            try { observer.OnRunEnd(runId, outcome); } catch { }
        }
    }

    public void OnMetric(string name, long value, IReadOnlyDictionary<string, string>? tags = null)
    {
        foreach (var observer in Observers)
        {
            try { observer.OnMetric(name, value, tags); } catch { }
        }
    }

    public void OnLog(StructuredLog entry)
    {
        foreach (var observer in Observers)
        {
            try { observer.OnLog(entry); } catch { }
        }
    }
}
