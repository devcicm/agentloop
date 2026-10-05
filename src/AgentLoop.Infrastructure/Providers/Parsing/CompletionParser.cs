using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentLoop.Parsing;


/// <summary>
/// CAPA 2 - traduce el JSON crudo del proveedor a tipos del harness.
/// Sin estado y sin red: se puede probar con fixtures. Puerto de tokenrouter/parsers.py.
/// </summary>
public static partial class CompletionParser
{
    private static readonly IReadOnlyList<ParsedToolCall> NoToolCalls = [];
    private static readonly IReadOnlyList<JsonElement> NoDeltas = [];

    public static ParsedCompletion Parse(JsonElement raw)
    {
        var content = ExtractContent(raw);
        var usage = ExtractUsage(raw);
        var thinking = ExtractThinking(raw);

        // Algunos backends no tienen campo aparte y encajan el razonamiento entre
        // <think>...</think> dentro del texto. Si se deja pasar, el usuario acaba leyendo
        // el razonamiento como si fuera la respuesta.
        if (thinking.IsEmpty && content is not null &&
            TrySplitInlineThinking(content, out var inline, out var cleaned))
        {
            thinking = ThinkingTrace.FromText(inline, ThinkingSource.InlineTags);
            content = cleaned.Length > 0 ? cleaned : null;
        }

        // El proveedor puede cobrar tokens de razonamiento sin ensenar ni una linea:
        // se registra como censurado en vez de fingir que no penso.
        if (thinking.IsEmpty && usage.ThinkingTokens > 0)
        {
            thinking = new ThinkingTrace(
                [new ThinkingBlock(ThinkingBlockKind.Redacted, string.Empty)],
                ThinkingSource.UsageOnly,
                usage.ThinkingTokens);
        }

        return new ParsedCompletion(
            Id: ReadString(raw, "id"),
            Model: ReadString(raw, "model"),
            Created: ReadInt64(raw, "created"),
            FinishReason: ExtractFinishReason(raw),
            Content: content,
            Thinking: thinking.WithTokenCount(usage.ThinkingTokens),
            ToolCalls: ExtractToolCalls(raw),
            Usage: usage,
            Raw: raw);
    }

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

    /// <summary>
    /// Razonamiento del modelo, tipado. Cubre las formas que se ven en la practica: el
    /// campo <c>reasoning_content</c> (string o array), bloques <c>thinking</c> dentro de
    /// <c>content</c>, y el caso censurado sin texto. El coste en tokens lo pone
    /// <see cref="Parse"/> a partir de usage, que es donde vive.
    /// </summary>
    public static ThinkingTrace ExtractThinking(JsonElement raw)
    {
        if (!TryGetMessage(raw, out var message))
        {
            return ThinkingTrace.Empty;
        }

        if (message.TryGetProperty("reasoning_content", out var reasoning))
        {
            switch (reasoning.ValueKind)
            {
                case JsonValueKind.String:
                    var text = reasoning.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return ThinkingTrace.FromText(text, ThinkingSource.ReasoningContent);
                    }

                    break;

                case JsonValueKind.Array:
                    // Sin "type" explicito: aqui todo lo que hay es razonamiento.
                    var reasoningBlocks = ReadThinkingBlocks(reasoning, defaultToThinking: true);
                    if (reasoningBlocks.Count > 0)
                    {
                        return new ThinkingTrace(reasoningBlocks, ThinkingSource.ReasoningContent, 0);
                    }

                    break;
            }
        }

        // Estilo Anthropic: los bloques de pensamiento viajan mezclados con el texto.
        if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            var blocks = ReadThinkingBlocks(content, defaultToThinking: false);
            if (blocks.Count > 0)
            {
                return new ThinkingTrace(blocks, ThinkingSource.ContentBlocks, 0);
            }
        }

        return ThinkingTrace.Empty;
    }

    /// <summary>
    /// Separa &lt;think&gt;...&lt;/think&gt; del texto de respuesta. Devuelve false si no
    /// habia nada que separar, para no tocar el content en el caso normal.
    /// </summary>
    public static bool TrySplitInlineThinking(string content, out string thinking, out string cleaned)
    {
        thinking = string.Empty;
        cleaned = content;

        if (!content.Contains("<think", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var matches = InlineThinkingRegex().Matches(content);
        if (matches.Count == 0)
        {
            return false;
        }

        thinking = string.Join("\n", matches.Select(m => m.Groups[1].Value.Trim())).Trim();
        cleaned = InlineThinkingRegex().Replace(content, string.Empty).Trim();
        return true;
    }

    /// <summary>Bloques de razonamiento de un array; ignora los que son texto normal.</summary>
    private static IReadOnlyList<ThinkingBlock> ReadThinkingBlocks(JsonElement array, bool defaultToThinking)
    {
        var blocks = new List<ThinkingBlock>();

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                if (defaultToThinking && item.GetString() is { Length: > 0 } loose)
                {
                    blocks.Add(new ThinkingBlock(ThinkingBlockKind.Thinking, loose));
                }

                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var type = ReadString(item, "type");
            ThinkingBlockKind kind;

            switch (type)
            {
                case "redacted_thinking" or "redacted":
                    kind = ThinkingBlockKind.Redacted;
                    break;

                case "thinking_summary" or "summary" or "reasoning_summary":
                    kind = ThinkingBlockKind.Summary;
                    break;

                case "thinking" or "reasoning":
                    kind = ThinkingBlockKind.Thinking;
                    break;

                case "" when defaultToThinking:
                    kind = ThinkingBlockKind.Thinking;
                    break;

                default:
                    continue;
            }

            var text = ReadString(item, "thinking");
            if (text.Length == 0)
            {
                text = ReadString(item, "text");
            }

            // Un bloque censurado sin texto sigue siendo evidencia de que penso.
            if (text.Length == 0 && kind != ThinkingBlockKind.Redacted)
            {
                continue;
            }

            var signature = ReadString(item, "signature");
            blocks.Add(new ThinkingBlock(kind, text, signature.Length == 0 ? null : signature));
        }

        return blocks;
    }

    [GeneratedRegex(@"<think(?:ing)?>(.*?)</think(?:ing)?>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineThinkingRegex();

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

        // El desglose de razonamiento cuelga de completion_tokens_details en el formato
        // OpenAI; algunos gateways lo suben un nivel.
        var thinkingTokens = usage.TryGetProperty("completion_tokens_details", out var details) &&
                             details.ValueKind == JsonValueKind.Object
            ? ReadInt32(details, "reasoning_tokens")
            : 0;

        if (thinkingTokens == 0)
        {
            thinkingTokens = ReadInt32(usage, "reasoning_tokens");
        }

        return new TokenUsage(
            ReadInt32(usage, "prompt_tokens"),
            ReadInt32(usage, "completion_tokens"),
            ReadInt32(usage, "total_tokens"),
            thinkingTokens);
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
            Thinking: ReadString(delta, "reasoning_content"),
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
