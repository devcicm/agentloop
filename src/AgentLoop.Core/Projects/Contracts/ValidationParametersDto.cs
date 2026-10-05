using System.Text.Json.Serialization;

namespace AgentLoop.Goals.Projects.Contracts;


public sealed record ValidationParametersDto
{
    public required IReadOnlyList<string> ArtifactIds { get; init; }
    public string? CommandProfile { get; init; }
    public string? Scenario { get; init; }
}
