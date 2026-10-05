
namespace AgentLoop.Kernel.Projects;

public sealed record ProjectExecutionDefinition(string Adapter, System.Text.Json.JsonElement? Docker = null,
    System.Text.Json.JsonElement? Settings = null);
