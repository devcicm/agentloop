using System.Text.Json;

namespace AgentLoop.Parsing;


/// <summary>Resultado de parsear una respuesta cruda de /chat/completions.</summary>
public sealed record ParsedCompletion(
    string Id,
    string Model,
    long Created,
    string FinishReason,
    string? Content,
    ThinkingTrace Thinking,
    IReadOnlyList<ParsedToolCall> ToolCalls,
    TokenUsage Usage,
    JsonElement Raw)
{
    public bool HasToolCalls => ToolCalls.Count > 0;

    /// <summary>El modelo expuso razonamiento en esta respuesta.</summary>
    public bool HasThinking => Thinking.Observed;

    /// <summary>
    /// El modelo gasto el presupuesto razonando y no llego a emitir contenido.
    /// Pasa con qwen3.8-max-free cuando max_tokens es bajo: hay que subirlo, no reintentar igual.
    /// </summary>
    public bool IsThinkingOnly => string.IsNullOrEmpty(Content) && Thinking.Observed;

    /// <summary>Bloque de tool_calls tal cual llego, para reenviarlo intacto en el siguiente turno.</summary>
    public JsonElement? RawToolCalls
    {
        get
        {
            if (Raw.ValueKind != JsonValueKind.Object || !Raw.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var message = choices[0];
            return message.TryGetProperty("message", out var msg) &&
                   msg.TryGetProperty("tool_calls", out var toolCalls) &&
                   toolCalls.ValueKind == JsonValueKind.Array
                ? toolCalls
                : null;
        }
    }
}
