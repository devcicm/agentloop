using System.Text.Json;

namespace AgentLoop.Provider;


/// <summary>Mensaje neutral de la conversacion; el proveedor adapta su representacion wire.</summary>
public sealed class ChatMessage
{

    public required string Role { get; init; }

    public string? Content { get; init; }

    public string? Name { get; init; }

    public string? ToolCallId { get; init; }

    /// <summary>Se reenvia crudo tal como lo devolvio el modelo; el gateway lo exige intacto.</summary>

    public JsonElement? ToolCalls { get; init; }

    public string? ReasoningContent { get; init; }

    public static ChatMessage FromSystem(string content) => new() { Role = ChatRoles.System, Content = content };

    public static ChatMessage FromUser(string content) => new() { Role = ChatRoles.User, Content = content };

    public static ChatMessage FromAssistant(string? content, JsonElement? toolCalls = null, string? reasoning = null)
        => new() { Role = ChatRoles.Assistant, Content = content, ToolCalls = toolCalls, ReasoningContent = reasoning };

    public static ChatMessage FromToolResult(string toolCallId, string toolName, string output)
        => new() { Role = ChatRoles.Tool, ToolCallId = toolCallId, Name = toolName, Content = output };
}
