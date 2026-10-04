using AgentLoop.Security;
using AgentLoop.Prompting;

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
