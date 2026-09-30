namespace AgentLoop.Concurrency;

/// <summary>
/// Reset event manual en version asincrona: una vez senalado se queda senalado hasta que
/// alguien lo baja con <see cref="Reset"/>, y todos los que esperan pasan a la vez.
///
/// Es el equivalente a ManualResetEventSlim, pero esperar aqui no bloquea un hilo: devuelve
/// una Task. Esa diferencia es la que permite que el agente espere a un proceso sin dejar
/// tieso su hilo, que es justo lo que necesita para seguir atendiendo eventos.
///
/// Usos en el harness: el cierre de un proceso (latch: muchos esperan, se queda abierto) y
/// la puerta de ejecucion (abierta = pasa; cerrada = las ejecuciones se encolan).
/// </summary>
public sealed class AsyncManualResetEvent
{
    private readonly object _gate = new();
    private volatile TaskCompletionSource _source = NewSource();

    public AsyncManualResetEvent(bool signaled = false)
    {
        if (signaled)
        {
            _source.TrySetResult();
        }
    }

    public bool IsSet => _source.Task.IsCompleted;

    /// <summary>Espera indefinidamente a que alguien lo senale.</summary>
    public Task WaitAsync(CancellationToken cancellationToken = default)
        => _source.Task.WaitAsync(cancellationToken);

    /// <summary>Espera con tope. Devuelve false si vencio el plazo sin senal.</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var task = _source.Task;
        if (task.IsCompleted)
        {
            return true;
        }

        try
        {
            await task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Set() => _source.TrySetResult();

    /// <summary>Baja la senal. Los que esperen a partir de ahora vuelven a quedarse esperando.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            if (_source.Task.IsCompleted)
            {
                _source = NewSource();
            }
        }
    }

    // Sin esto las continuaciones correrian sincronas dentro del Set(), es decir dentro del
    // handler de salida del proceso o del lock de quien senala: la receta del interbloqueo.
    private static TaskCompletionSource NewSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public override string ToString() => IsSet ? "senalado" : "esperando";
}

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
