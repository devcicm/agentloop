using System.Text.Json;

namespace AgentLoop.Provider;


/// <summary>
/// Solicitud neutral del agente. El adaptador del proveedor decide los nombres y campos del payload.
/// Es un record para poder derivar variantes con 'with' (stream on/off, thinking fuera)
/// sin volver a copiar campo a campo y olvidarse de uno.
/// </summary>
public sealed record ChatRequest
{

    public required string Model { get; init; }

    public required IReadOnlyList<ChatMessage> Messages { get; init; }

    public IReadOnlyList<ToolDefinition>? Tools { get; init; }

    public string? ToolChoice { get; init; }

    public bool? ParallelToolCalls { get; init; }

    public ResponseFormat? ResponseFormat { get; init; }

    public double? Temperature { get; init; }

    public double? TopP { get; init; }

    public int? MaxTokens { get; init; }

    public IReadOnlyList<string>? Stop { get; init; }

    public bool? Stream { get; init; }

    /// <summary>Solo viaja cuando la peticion es de streaming; el cliente lo omite si no.</summary>

    public StreamOptions? StreamOptions { get; init; }

    /// <summary>
    /// Razonamiento explicito. Va en la raiz del payload, no dentro de un mensaje, y el
    /// modelo devuelve lo que penso en <c>message.reasoning_content</c>. Null = no se manda
    /// el campo, que es lo que esperan los modelos que no lo entienden.
    /// </summary>

    public string? ReasoningEffort { get; init; }

    public ThinkingOptions? Thinking { get; init; }

    public IReadOnlyDictionary<string, object?>? ChatTemplateKwargs { get; init; }
}
