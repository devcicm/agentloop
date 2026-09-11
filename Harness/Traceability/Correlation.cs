namespace AgentLoop.Harness.Traceability;

/// <summary>
/// Holder de <see cref="CorrelationContext"/> basado en <see cref="AsyncLocal{T}"/>.
///
/// El reactor, el supervisor y los tools viven en distintos hilos del pool y se cruzan
/// con awaits profundos. Sin AsyncLocal, pasar el TraceId por parametro contaminaria cada
/// firma; con AsyncLocal, lo de arriba establece el contexto y lo de abajo lo lee sin
/// saber como se propago.
///
/// Para no contaminar el resto del harness, este tipo es opcional: si nadie lo
/// establece, <see cref="Current"/> devuelve null y el codigo que lo usa cae a un
/// identificador vacio sin lanzar.
/// </summary>
public static class Correlation
{
    private static readonly AsyncLocal<CorrelationContext?> _current = new();

    /// <summary>Contexto activo en este flujo logico, o null si nadie lo establecio.</summary>
    public static CorrelationContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }

    /// <summary>Restablece el contexto a null. Pensado para pruebas.</summary>
    public static void Reset() => _current.Value = null;

    /// <summary>Establece un contexto raiz y lo devuelve. Pensado para arrancar Runs.</summary>
    public static CorrelationContext Start()
    {
        var root = CorrelationContext.Root();
        _current.Value = root;
        return root;
    }
}
