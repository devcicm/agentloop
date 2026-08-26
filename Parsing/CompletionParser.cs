using System.Text.Json;

namespace AgentLoop.Parsing;

/// <summary>
/// CAPA 2 - traduce el JSON crudo del proveedor a tipos del harness.
/// Sin estado y sin red: se puede probar con fixtures. Puerto de tokenrouter/parsers.py.
/// </summary>
public static class CompletionParser
{
    private static readonly IReadOnlyList<ParsedToolCall> NoToolCalls = [];
    private static readonly IReadOnlyList<JsonElement> NoDeltas = [];

    public static ParsedCompletion Parse(JsonElement raw) => new(
        Id: ReadString(raw, "id"),
        Model: ReadString(raw, "model"),
        Created: ReadInt64(raw, "created"),
        FinishReason: ExtractFinishReason(raw),
        Content: ExtractContent(raw),
        Reasoning: ExtractReasoning(raw),
        ToolCalls: ExtractToolCalls(raw),
        Usage: ExtractUsage(raw),
        Raw: raw);

    /// <summary>
    /// Texto del primer choice. Acepta content como string y como array de partes
    /// ({"type":"text"} / {"type":"output_text"}), que es lo que devuelven algunos backends.
    /// </summary>
    public static string? ExtractContent(JsonElement raw)
    {
        if (!TryGetMessage(raw, out var message) || !message.TryGetProperty("content", out var content))
        {
            return null;
        }

        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return content.GetString();

            case JsonValueKind.Array:
                var parts = new List<string>();
                foreach (var chunk in content.EnumerateArray())
                {
                    if (chunk.ValueKind == JsonValueKind.String)
                    {
                        parts.Add(chunk.GetString() ?? string.Empty);
                    }
                    else if (chunk.ValueKind == JsonValueKind.Object)
                    {
                        var type = ReadString(chunk, "type");
                        if (type is "text" or "output_text")
                        {
                            parts.Add(ReadString(chunk, "text"));
                        }
                    }
                }

                return parts.Count > 0 ? string.Concat(parts) : null;

            default:
                return null;
        }
    }

    /// <summary>Cadena de razonamiento si el modelo la expone (reasoning_content), si no null.</summary>
    public static string? ExtractReasoning(JsonElement raw)
    {
        if (!TryGetMessage(raw, out var message) ||
            !message.TryGetProperty("reasoning_content", out var reasoning) ||
            reasoning.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = reasoning.GetString();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Tool calls normalizadas. Los argumentos llegan como string JSON: si no parsea,
    /// se marca el fallo en lugar de lanzar, para poder devolverle el error al modelo.
    /// </summary>
    public static IReadOnlyList<ParsedToolCall> ExtractToolCalls(JsonElement raw)
    {
        if (!TryGetMessage(raw, out var message))
        {
            return NoToolCalls;
        }

        var result = new List<ParsedToolCall>();

        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                var function = call.TryGetProperty("function", out var fn) ? fn : default;
                result.Add(BuildToolCall(ReadString(call, "id"), function));
            }
        }

        // Fallback al campo legacy function_call, que algunos backends siguen usando.
        if (result.Count == 0 && message.TryGetProperty("function_call", out var legacy) &&
            legacy.ValueKind == JsonValueKind.Object)
        {
            result.Add(BuildToolCall(string.Empty, legacy));
        }

        return result.Count > 0 ? result : NoToolCalls;
    }

    public static string ExtractFinishReason(JsonElement raw)
        => TryGetChoice(raw, out var choice) ? ReadString(choice, "finish_reason") : string.Empty;

    public static TokenUsage ExtractUsage(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return TokenUsage.Empty;
        }

        return new TokenUsage(
            ReadInt32(usage, "prompt_tokens"),
            ReadInt32(usage, "completion_tokens"),
            ReadInt32(usage, "total_tokens"));
    }

    /// <summary>Normaliza un chunk de streaming (objeto completo, no solo el delta).</summary>
    public static StreamDelta ParseStreamDelta(JsonElement chunk)
    {
        if (!TryGetChoice(chunk, out var choice) || !choice.TryGetProperty("delta", out var delta) ||
            delta.ValueKind != JsonValueKind.Object)
        {
            return new StreamDelta(null, string.Empty, string.Empty, NoDeltas, ExtractFinishReasonFrom(chunk));
        }

        var toolCallDeltas = new List<JsonElement>();
        if (delta.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
        {
            toolCallDeltas.AddRange(calls.EnumerateArray());
        }

        return new StreamDelta(
            Role: delta.TryGetProperty("role", out var role) && role.ValueKind == JsonValueKind.String
                ? role.GetString()
                : null,
            Content: ReadString(delta, "content"),
            Reasoning: ReadString(delta, "reasoning_content"),
            ToolCallDeltas: toolCallDeltas.Count > 0 ? toolCallDeltas : NoDeltas,
            FinishReason: ExtractFinishReasonFrom(chunk));
    }

    public static bool TryExtractError(JsonElement raw, out ProviderError error)
    {
        error = null!;
        if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("error", out var node))
        {
            return false;
        }

        if (node.ValueKind == JsonValueKind.String)
        {
            error = new ProviderError(node.GetString() ?? string.Empty, string.Empty, string.Empty);
            return true;
        }

        if (node.ValueKind == JsonValueKind.Object)
        {
            error = new ProviderError(ReadString(node, "message"), ReadString(node, "type"), ReadString(node, "code"));
            return true;
        }

        return false;
    }

    private static ParsedToolCall BuildToolCall(string id, JsonElement function)
    {
        var name = function.ValueKind == JsonValueKind.Object ? ReadString(function, "name") : string.Empty;
        var rawArguments = "{}";

        if (function.ValueKind == JsonValueKind.Object && function.TryGetProperty("arguments", out var arguments))
        {
            // Casi siempre es un string con JSON dentro; algunos backends mandan el objeto directo.
            if (arguments.ValueKind == JsonValueKind.String)
            {
                rawArguments = arguments.GetString() ?? "{}";
            }
            else if (arguments.ValueKind != JsonValueKind.Undefined && arguments.ValueKind != JsonValueKind.Null)
            {
                return new ParsedToolCall(id, name, arguments.Clone(), arguments.GetRawText(), false);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(rawArguments);
            return new ParsedToolCall(id, name, doc.RootElement.Clone(), rawArguments, false);
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("{}");
            return new ParsedToolCall(id, name, empty.RootElement.Clone(), rawArguments, true);
        }
    }

    private static string ExtractFinishReasonFrom(JsonElement chunk)
        => TryGetChoice(chunk, out var choice) ? ReadString(choice, "finish_reason") : string.Empty;

    private static bool TryGetChoice(JsonElement raw, out JsonElement choice)
    {
        choice = default;
        if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return false;
        }

        choice = choices[0];
        return true;
    }

    private static bool TryGetMessage(JsonElement raw, out JsonElement message)
    {
        message = default;
        if (!TryGetChoice(raw, out var choice) || !choice.TryGetProperty("message", out var msg) ||
            msg.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        message = msg;
        return true;
    }

    private static string ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static int ReadInt32(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt32(out var number)
            ? number
            : 0;

    private static long ReadInt64(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out var number)
            ? number
            : 0;
}
