using System.Text.Json;

namespace AgentLoop.Provider;


public sealed class ResponseFormat
{

    public required string Type { get; init; }

    public static ResponseFormat JsonObject => new() { Type = "json_object" };
}
