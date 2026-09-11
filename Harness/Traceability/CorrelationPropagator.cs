namespace AgentLoop.Harness.Traceability;

/// <summary>
/// Crea spans hijos al cruzar una frontera async. Pensado para envolver con
/// <see cref="IAsyncDisposable"/> y asi restaurar el contexto previo al salir.
///
/// Como todavia no hay propagacion a traves del bus ni de los HttpClient, este
/// helper solo cubre el lado del reactor: el await que viene de una llamada al
/// modelo. El supervisor de procesos, cuando publique eventos desde sus hilos
/// de captura, leera el contexto que estuviera vivo cuando se lanzo el proceso;
/// si nadie lo establecio, el evento queda sin TraceId, que es el comportamiento
/// degradado esperado.
/// </summary>
public static class CorrelationPropagator
{
    /// <summary>
    /// Ejecuta <paramref name="work"/> con un span hijo del actual. Si no hay
    /// contexto activo, crea uno raiz.
    /// </summary>
    public static async Task<T> InSpanAsync<T>(string name, Func<Task<T>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var parent = Correlation.Current;
        var child = parent is null ? CorrelationContext.Root() : parent.Value.Child();
        var previous = Correlation.Current;
        Correlation.Current = child;
        try
        {
            return await work().ConfigureAwait(false);
        }
        finally
        {
            Correlation.Current = previous;
        }
    }

    /// <summary>Variante sincrona que no necesita Task.</summary>
    public static T InSpan<T>(string name, Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        var parent = Correlation.Current;
        var child = parent is null ? CorrelationContext.Root() : parent.Value.Child();
        var previous = Correlation.Current;
        Correlation.Current = child;
        try
        {
            return work();
        }
        finally
        {
            Correlation.Current = previous;
        }
    }
}
