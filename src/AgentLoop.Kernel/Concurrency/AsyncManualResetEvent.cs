
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
