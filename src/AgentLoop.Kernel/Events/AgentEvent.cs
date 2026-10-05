namespace AgentLoop.Events;


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
}
