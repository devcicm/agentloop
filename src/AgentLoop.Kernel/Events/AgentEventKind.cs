
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
    PermisosRevocados,
}
