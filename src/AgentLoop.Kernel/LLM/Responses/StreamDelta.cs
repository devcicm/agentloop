using System.Text.Json;

namespace AgentLoop.Parsing;


/// <summary>Un delta de streaming ya normalizado.</summary>
public sealed record StreamDelta(
    string? Role,
    string Content,
    string Thinking,
    IReadOnlyList<JsonElement> ToolCallDeltas,
    string FinishReason)
{
    /// <summary>En streaming el razonamiento llega troceado, asi que aqui es texto suelto
    /// y no un <see cref="ThinkingTrace"/>: los bloques se arman al cerrar el mensaje.</summary>
    public bool HasThinking => Thinking.Length > 0;

    public bool IsEmpty => Content.Length == 0 && Thinking.Length == 0 && ToolCallDeltas.Count == 0;
}
