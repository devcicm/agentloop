using System.Text.Json;

namespace AgentLoop.Parsing;

/// <summary>Consumo de tokens de una llamada; se acumula a lo largo del loop.</summary>
public readonly record struct TokenUsage(int PromptTokens, int CompletionTokens, int TotalTokens)
{
    public static TokenUsage Empty => default;

    public TokenUsage Add(TokenUsage other) => new(
        PromptTokens + other.PromptTokens,
        CompletionTokens + other.CompletionTokens,
        TotalTokens + other.TotalTokens);

    public override string ToString() => $"prompt={PromptTokens} completion={CompletionTokens} total={TotalTokens}";
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
    string? Reasoning,
    IReadOnlyList<ParsedToolCall> ToolCalls,
    TokenUsage Usage,
    JsonElement Raw)
{
    public bool HasToolCalls => ToolCalls.Count > 0;

    /// <summary>
    /// El modelo gasto el presupuesto razonando y no llego a emitir contenido.
    /// Pasa con qwen3.8-max-free cuando max_tokens es bajo: hay que subirlo, no reintentar igual.
    /// </summary>
    public bool IsReasoningOnly => string.IsNullOrEmpty(Content) && !string.IsNullOrEmpty(Reasoning);

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
    string Reasoning,
    IReadOnlyList<JsonElement> ToolCallDeltas,
    string FinishReason)
{
    public bool IsEmpty => Content.Length == 0 && Reasoning.Length == 0 && ToolCallDeltas.Count == 0;
}
