
namespace AgentLoop.Concurrency;


/// <summary>
/// Reset event automatico: cada <see cref="Set"/> libera exactamente a un esperador y la
/// senal se consume. Si nadie espera, queda una senal pendiente (no se pierde el aviso).
///
/// Es lo que despierta al agente cuando entra algo al bus: una senal, un ciclo de reaccion.
/// </summary>
public sealed class AsyncAutoResetEvent
{
    private readonly object _gate = new();
    private readonly LinkedList<TaskCompletionSource> _waiters = new();
    private bool _signaled;

    public AsyncAutoResetEvent(bool signaled = false) => _signaled = signaled;

    public bool IsSignaled
    {
        get
        {
            lock (_gate)
            {
                return _signaled;
            }
        }
    }

    public int Waiters
    {
        get
        {
            lock (_gate)
            {
                return _waiters.Count;
            }
        }
    }

    public void Set()
    {
        lock (_gate)
        {
            // Los esperadores que se fueron por timeout quedan cancelados en la cola:
            // se descartan hasta encontrar uno vivo, si no la senal se perderia con ellos.
            while (_waiters.Count > 0)
            {
                var candidate = _waiters.First!.Value;
                _waiters.RemoveFirst();
                if (candidate.TrySetResult())
                {
                    return;
                }
            }

            _signaled = true;
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken = default)
        => WaitCoreAsync(Timeout.InfiniteTimeSpan, cancellationToken);

    /// <summary>Espera con tope. Devuelve false si vencio el plazo sin senal.</summary>
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => WaitCoreAsync(timeout, cancellationToken);

    private async Task<bool> WaitCoreAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TaskCompletionSource waiter;
        LinkedListNode<TaskCompletionSource> registration;

        lock (_gate)
        {
            if (_signaled)
            {
                _signaled = false;
                return true;
            }

            waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            registration = _waiters.AddLast(waiter);
        }

        try
        {
            if (timeout == Timeout.InfiniteTimeSpan)
                await waiter.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            else
                await waiter.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Si Set gano justo cuando el token se cancelo, devolvemos la senal
            // al siguiente esperador en vez de consumirla sin atenderla.
            if (!waiter.TrySetCanceled() && waiter.Task.IsCompletedSuccessfully) Set();
            throw;
        }
        catch (TimeoutException)
        {
            // Si no se deja cancelar es que alguien la senalo en la carrera: la senal ya
            // es nuestra y perderla dejaria un evento sin atender.
            return !waiter.TrySetCanceled();
        }
        finally
        {
            lock (_gate) if (registration.List is not null) _waiters.Remove(registration);
        }
    }
}
