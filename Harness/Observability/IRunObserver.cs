using AgentLoop.Harness.Traceability;

namespace AgentLoop.Harness.Observability;

/// <summary>
/// Contrato que cualquier sink debe implementar para engancharse al harness.
/// El bus, el runner y el reactor llaman a estos callbacks en puntos estables
/// del ciclo de vida (cada step, cada metrica, cada log); un observer decide
/// que hacer (pintar en consola, escribir a JSON, emitir a Prometheus, etc).
///
/// Implementar este interface NO requiere tocar el resto del harness.
/// </summary>
public interface IRunObserver
{
    /// <summary>Se invoca al inicio del Run, antes de cualquier step.</summary>
    void OnRunStart(Guid runId) { }

    /// <summary>Se invoca al cierre del Run, haya terminado bien o mal.</summary>
    void OnRunEnd(Guid runId, string outcome) { }

    /// <summary>Un Step acaba de ocurrir (model call, tool call, transicion, etc).</summary>
    void OnStep(StepEnvelope step) { }

    /// <summary>Un contador de metricas cambia de valor.</summary>
    void OnMetric(string name, long value, IReadOnlyDictionary<string, string>? tags = null) { }

    /// <summary>Una linea de log estructurado esta lista para emitirse.</summary>
    void OnLog(StructuredLog entry) { }
}

/// <summary>Sobre minimo para que un Step se mueva por el bus de observers.</summary>
public sealed record StepEnvelope(
    Guid RunId,
    long Sequence,
    DateTimeOffset At,
    string Kind,
    CorrelationContext? Correlation,
    IReadOnlyDictionary<string, object?> Data);
