using System.Text.Json;

namespace AgentLoop.Parsing;

/// <summary>
/// Consumo de tokens de una llamada; se acumula a lo largo del loop.
/// <see cref="ThinkingTokens"/> es la parte de completion que se fue en razonar: sale de
/// usage.completion_tokens_details.reasoning_tokens y ya viene incluida en CompletionTokens.
/// </summary>
public readonly record struct TokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int ThinkingTokens = 0)
{
    public static TokenUsage Empty => default;

    public TokenUsage Add(TokenUsage other) => new(
        PromptTokens + other.PromptTokens,
        CompletionTokens + other.CompletionTokens,
        TotalTokens + other.TotalTokens,
        ThinkingTokens + other.ThinkingTokens);

    public override string ToString()
    {
        var thinking = ThinkingTokens > 0 ? $" (thinking={ThinkingTokens})" : string.Empty;
        return $"prompt={PromptTokens} completion={CompletionTokens}{thinking} total={TotalTokens}";
    }
}

/// <summary>
/// Una tool call ya normalizada. <see cref="Arguments"/> viene deserializado desde el string
/// JSON que manda el proveedor; si ese string estaba roto, <see cref="ArgumentsParseFailed"/>
/// queda en true y <see cref="RawArguments"/> conserva el texto original.
/// </summary>
public sealed record ParsedToolCall(
    string Id,
    string Name,
    JsonElement Arguments,
    string RawArguments,
    bool ArgumentsParseFailed)
{
    public override string ToString() => $"{Name}({RawArguments})";
}

/// <summary>Error estructurado devuelto por el gateway.</summary>
public sealed record ProviderError(string Message, string Type, string Code);

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
