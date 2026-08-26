using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Provider;

/// <summary>Roles del protocolo OpenAI-compatible.</summary>
public static class ChatRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

/// <summary>Un mensaje de la conversacion, tal como viaja en el payload.</summary>
public sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    public string? Content { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("tool_call_id")]
    public string? ToolCallId { get; init; }

    /// <summary>Se reenvia crudo tal como lo devolvio el modelo; el gateway lo exige intacto.</summary>
    [JsonPropertyName("tool_calls")]
    public JsonElement? ToolCalls { get; init; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; init; }

    public static ChatMessage FromSystem(string content) => new() { Role = ChatRoles.System, Content = content };

    public static ChatMessage FromUser(string content) => new() { Role = ChatRoles.User, Content = content };

    public static ChatMessage FromAssistant(string? content, JsonElement? toolCalls = null, string? reasoning = null)
        => new() { Role = ChatRoles.Assistant, Content = content, ToolCalls = toolCalls, ReasoningContent = reasoning };

    public static ChatMessage FromToolResult(string toolCallId, string toolName, string output)
        => new() { Role = ChatRoles.Tool, ToolCallId = toolCallId, Name = toolName, Content = output };
}

/// <summary>Esquema de una funcion expuesta al modelo.</summary>
public sealed class ToolFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>JSON Schema de los parametros.</summary>
    [JsonPropertyName("parameters")]
    public required JsonElement Parameters { get; init; }
}

public sealed class ToolDefinition
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required ToolFunction Function { get; init; }
}

public sealed class ResponseFormat
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    public static ResponseFormat JsonObject => new() { Type = "json_object" };
}

/// <summary>
/// Ajustes del modo streaming. <c>include_usage</c> hace que llegue un ultimo chunk con
/// el consumo de tokens: sin el, una respuesta en streaming no se puede contabilizar.
/// </summary>
public sealed class StreamOptions
{
    public static readonly StreamOptions WithUsage = new() { IncludeUsage = true };

    [JsonPropertyName("include_usage")]
    public bool? IncludeUsage { get; init; }
}

/// <summary>
/// Payload de POST /chat/completions. Las propiedades nulas no se serializan.
/// Es un record para poder derivar variantes con 'with' (stream on/off, thinking fuera)
/// sin volver a copiar campo a campo y olvidarse de uno.
/// </summary>
public sealed record ChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    public string? ToolChoice { get; init; }

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; init; }

    [JsonPropertyName("response_format")]
    public ResponseFormat? ResponseFormat { get; init; }

    [JsonPropertyName("temperature")]
    public double? Temperature { get; init; }

    [JsonPropertyName("top_p")]
    public double? TopP { get; init; }

    [JsonPropertyName("max_tokens")]
    public int? MaxTokens { get; init; }

    [JsonPropertyName("stop")]
    public IReadOnlyList<string>? Stop { get; init; }

    [JsonPropertyName("stream")]
    public bool? Stream { get; init; }

    /// <summary>Solo viaja cuando la peticion es de streaming; el cliente lo omite si no.</summary>
    [JsonPropertyName("stream_options")]
    public StreamOptions? StreamOptions { get; init; }

    /// <summary>
    /// Razonamiento explicito. Va en la raiz del payload, no dentro de un mensaje, y el
    /// modelo devuelve lo que penso en <c>message.reasoning_content</c>. Null = no se manda
    /// el campo, que es lo que esperan los modelos que no lo entienden.
    /// </summary>
    [JsonPropertyName("thinking")]
    public ThinkingOptions? Thinking { get; init; }
}

/// <summary>Entrada de GET /models.</summary>
public sealed record ModelInfo(
    string Id,
    string OwnedBy,
    long Created,
    IReadOnlyList<string> SupportedEndpointTypes,
    string Tags)
{
    private static readonly string[] ChatEndpoints =
        ["openai", "openai-response", "anthropic", "anthropic-compatible", "gemini"];

    public bool IsFree =>
        Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase) ||
        Id.EndsWith("-free", StringComparison.OrdinalIgnoreCase) ||
        Id.Contains(":free", StringComparison.OrdinalIgnoreCase);

    public bool IsText => Tags.Contains("Text", StringComparison.OrdinalIgnoreCase);

    public bool SupportsChat =>
        SupportedEndpointTypes.Any(e => ChatEndpoints.Contains(e, StringComparer.OrdinalIgnoreCase));
}
