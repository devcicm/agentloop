using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Security.Contracts;


/// <summary>Reserved metadata belongs to the harness; extension fields remain external data.</summary>
public sealed class ToolResultDto
{
    [JsonPropertyName("_harness")]
    public required ToolDataMetadataDto Metadata { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Data { get; init; } = new();
}
