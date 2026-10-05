
namespace AgentLoop.Harness.Traceability;


/// <summary>
/// Identidad de una unidad de trazabilidad. Se crea un <see cref="CorrelationContext"/>
/// al arrancar un Run y un <see cref="SpanId"/> nuevo cada vez que se cruza una frontera
/// async significativa (publicar un evento, ejecutar un tool, llamar al modelo).
///
/// La idea no es reproducir OpenTelemetry: con un GUID por Run y un SpanId por paso
/// alcanza para correlacionar Steps, eventos del bus y logs sin tener que sacar
/// librerias externas.
/// </summary>
public readonly record struct CorrelationContext(
    Guid TraceId,
    Guid SpanId,
    Guid? ParentSpanId)
{
    /// <summary>Contexto raiz: un nuevo TraceId, sin padre.</summary>
    public static CorrelationContext Root()
        => new(Guid.NewGuid(), Guid.NewGuid(), null);

    /// <summary>Hijo del contexto actual: conserva TraceId, genera SpanId nuevo y registra al actual como padre.</summary>
    public CorrelationContext Child()
        => new(TraceId, Guid.NewGuid(), SpanId);

    public bool HasParent => ParentSpanId.HasValue;

    public override string ToString()
        => HasParent
            ? $"trace={TraceId:N} span={SpanId:N} parent={ParentSpanId:N}"
            : $"trace={TraceId:N} span={SpanId:N} (root)";
}
