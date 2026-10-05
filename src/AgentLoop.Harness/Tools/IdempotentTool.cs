using System.Text.Json;
namespace AgentLoop.Tools;


/// <summary>
/// Decorator que envuelve cualquier <see cref="ITool"/> con cache por hash:
/// la primera llamada ejecuta el handler y guarda el resultado; las
/// siguientes con la misma combinacion (tool + argumentos) devuelven el
/// resultado cacheado sin reejecutar.
///
/// Pensado para tools read-only (leer, procesos, kill, observar) cuyo
/// resultado es estable entre llamadas. NO se aplica a ejecutar ni
/// lanzar porque tienen efectos reales y un retry legitimo del modelo
/// debe poder reejecutarlas.
///
/// La clave se calcula sobre el JSON de argumentos. Cambiar el orden de
/// las claves produce la misma clave (el calculo ordena). El cache es
/// bounded LRU: cuando se llena, descarta la entrada menos usada
/// recientemente.
/// </summary>
public sealed class IdempotentTool : ITool
{
    private readonly ITool _inner;
    private readonly IdempotencyCache _cache;

    public IdempotentTool(ITool inner, IdempotencyCache? cache = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? new IdempotencyCache(capacity: 256);
    }

    public string Name => _inner.Name;

    public string Description => _inner.Description;

    public JsonElement Parameters => _inner.Parameters;

    public IdempotencyCache Cache => _cache;

    /// <summary>
    /// Cantidad de hits servidos desde cache desde que se creo el wrapper.
    /// Util para telemetry y para el self-test.
    /// </summary>
    public long Hits { get; private set; }

    /// <summary>Cantidad de misses: llamadas que si ejecutaron el handler.</summary>
    public long Misses { get; private set; }

    public ValueTask<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var key = IdempotencyKey.ComputeKey(Name, arguments.GetRawText());

        // Singleflight: el cache guarda un Lazy<Task<string>>. La primera
        // llamada ejecuta el factory; las siguientes con la misma clave
        // esperan al mismo Task y obtienen el mismo resultado. Asi N hilos
        // en paralelo producen 1 ejecucion del tool subyacente.
        // Para llevar el contador Hits/Misses, miramos si la entrada ya
        // existia antes de pedirla: pero GetOrAdd no expone esa info
        // directamente, asi que despues del await comparamos el timestamp
        // de la entrada con el actual.
        var before = _cache.GetCount();

        Task<string> task;
        try
        {
            task = _cache.GetOrAddTaskAsync(key, () => _inner.InvokeAsync(arguments, cancellationToken).AsTask());
        }
        catch
        {
            // El factory lanzo sincronico (el Lazy lo cachearia como fallo
            // pegado): liberar y propagar para que el reintento ejecute.
            _cache.Remove(key);
            throw;
        }

        // Agentico: la clave se registra despues del efecto, o se libera si
        // el efecto falla. Sin esto, un fallo transitorio quedaria cacheado
        // para siempre y el reintento recibiria el mismo error sin ejecutar.
        _ = task.ContinueWith(
            t =>
            {
                if (t.IsFaulted)
                {
                    _ = t.Exception;
                }

                if (t.IsFaulted || t.IsCanceled)
                {
                    _cache.Remove(key);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        var after = _cache.GetCount();

        if (after > before)
        {
            // Entrada nueva: factory se va a ejecutar (o se esta ejecutando).
            Misses++;
        }
        else
        {
            Hits++;
        }

        return new ValueTask<string>(task);
    }
}
