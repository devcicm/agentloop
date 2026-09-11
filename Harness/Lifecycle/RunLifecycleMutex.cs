using AgentLoop.Harness.Concurrency;

namespace AgentLoop.Harness.Lifecycle;

/// <summary>
/// Mutex del Run: serializa las transiciones del lifecycle para que dos
/// publicaciones concurrentes no puedan dejar el Run en un estado
/// incoherente. Sin el, dos transiciones racing (Running -> Succeeded
/// y Running -> Failed a la vez) harian que la ultima publicacion
/// gane sin validar la tabla de transiciones validas.
///
/// Es un AsyncLock por Run: cada Run tiene su propio mutex, asi Runs
/// distintos transicionan en paralelo.
/// </summary>
public sealed class RunLifecycleMutex
{
    private readonly AsyncLock _gate = new();

    public async Task<TryTransitionResult> TryTransitionAsync(
        Guid runId,
        ILifecycleObserver observer,
        RunLifecycleState from,
        RunLifecycleState to,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? payload = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observer);

        using var _ = await _gate.LockAsync(cancellationToken).ConfigureAwait(false);

        if (!LifecycleTransitions.IsValid(from, to))
        {
            return TryTransitionResult.Rejected(from, to);
        }

        observer.OnLifecycle(new LifecycleEvent(runId, from, to, DateTimeOffset.Now, reason, payload));
        return TryTransitionResult.Ok();
    }
}

/// <summary>Resultado de TryTransitionAsync: si fue aceptada o por que se rechazo.</summary>
public readonly record struct TryTransitionResult(bool IsAccepted, RunLifecycleState From, RunLifecycleState To, string? Reason)
{
    public static TryTransitionResult Ok() => new(true, default, default, null);
    public static TryTransitionResult Rejected(RunLifecycleState from, RunLifecycleState to)
        => new(false, from, to, $"transicion invalida de {from} a {to}");
}
