using AgentLoop.Security;
using AgentLoop.Prompting;

namespace AgentLoop.Events;

/// <summary>Que clase de suceso es. El agente reacciona a esto, no a turnos de conversacion.</summary>
public enum AgentEventKind
{
    /// <summary>Entra el objetivo al sistema.</summary>
    Objetivo,

    /// <summary>El agente jugo una tarjeta de estado y el sistema la acepto.</summary>
    Tarjeta,

    /// <summary>Una tarjeta invalida: fuera de orden o sin los campos que exige.</summary>
    TarjetaRechazada,

    /// <summary>Arranco un proceso.</summary>
    ProcesoIniciado,

    /// <summary>Hay salida nueva de un proceso vivo.</summary>
    ProcesoSalida,

    /// <summary>Un proceso termino, con su codigo de salida.</summary>
    ProcesoFin,

    /// <summary>Un patron vigilado aparecio en la salida.</summary>
    Observador,

    /// <summary>Silencio: ni eventos ni procesos vivos. Sirve para no dormirse.</summary>
    Latido,

    /// <summary>Texto del modelo o nota del sistema.</summary>
    Aviso,

    /// <summary>Algo fallo: politica, proceso que no arranca, herramienta rota.</summary>
    Error,

    /// <summary>Una herramienta termino; sus resultados ya estan en el contexto.</summary>
    HerramientaFinalizada,

    /// <summary>Se pidio parar.</summary>
    Cancelacion,
    ModeloSolicitado,
    ModeloResultado,
    HerramientaSolicitada,
    HerramientaResultado,
    RazonamientoRequerido,
    DeadlineAlcanzado,
    Pausa,
    Reanudacion,
    ConsultaEstado,
    Estado,
    ObjetivoCumplido,
    ObjetivoFallido,
    EventoConfirmado,
    DetencionAgotada,
    AprobacionSolicitada,
    Aprobacion,
    ProyectoAdmitido,
    TrabajoHabilitado,
    ValidacionSolicitada,
    ValidacionFinalizada,
    ValidacionFallida,
    TrabajoAceptado,
    ReconciliacionRequerida,
}

/// <summary>
/// Un suceso del sistema. Es la unidad que mueve el harness: el agente despierta con
/// eventos, no porque a alguien le toque hablar.
/// </summary>
/// <param name="Sequence">Orden global de publicacion, para poder citar eventos sin ambiguedad.</param>
/// <param name="Data">JSON opcional con el detalle estructurado del suceso.</param>
public sealed record AgentEvent(
    long Sequence,
    DateTimeOffset At,
    AgentEventKind Kind,
    string Source,
    string Text,
    string? Data = null)
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public Guid RunId { get; init; }
    public Guid? CausationId { get; init; }
    /// <summary>Linea corta para la traza de consola.</summary>
    public override string ToString()
        => TextSanitizer.Truncate(DataGuard.Display($"#{Sequence} {At:HH:mm:ss} {Kind} [{Source}] {Text}")
            .Replace("\n", "\\n"), 4096);

    /// <summary>Como se le cuenta el suceso al modelo.</summary>
    public string ToPrompt()
    {
        var data = Data is { Length: > 0 } ? $" | datos: {Data}" : string.Empty;
        return $"- #{Sequence} [{Kind}] ({Source}) {Text}{data}";
    }
}
