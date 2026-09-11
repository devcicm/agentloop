namespace AgentLoop.Harness.Concurrency;

/// <summary>
/// Mutex asincrono: el equivalente async de <c>lock</c>. Garantiza exclusion
/// mutua entre callers sin bloquear hilos del pool: quien pide el lock se
/// queda en una Task hasta que el owner lo libera.
///
/// Lo que el AsyncManualResetEvent hace para esperar senales, esto lo hace
/// para exclusion mutua: serializa secciones criticas (modificar el ledger,
/// transicionar el lifecycle del Run, escribir el Snapshot) manteniendo el
/// modelo async de C#.
///
/// Uso tipico:
/// <code>
/// private static readonly AsyncLock _gate = new();
/// using var guard = await _gate.LockAsync(ct);
/// // seccion critica aqui
/// </code>
/// </summary>
public sealed class AsyncLock
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool IsHeld => _semaphore.CurrentCount == 0;

    /// <summary>Espera a adquirir el lock y devuelve un guard que lo libera al Dispose.</summary>
    public async Task<LockGuard> LockAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new LockGuard(_semaphore);
    }

    public struct LockGuard : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _released;

        internal LockGuard(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
            _released = false;
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _semaphore.Release();
            _released = true;
        }
    }
}
