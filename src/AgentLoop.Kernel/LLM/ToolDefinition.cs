using System.Text.Json;

namespace AgentLoop.Provider;


public sealed class ToolDefinition
{

    public string Type { get; init; } = "function";

    public required ToolFunction Function { get; init; }
}
