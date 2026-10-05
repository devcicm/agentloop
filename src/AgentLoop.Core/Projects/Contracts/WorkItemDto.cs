using System.Text.Json.Serialization;

namespace AgentLoop.Goals.Projects.Contracts;


public sealed record WorkItemDto
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> DependsOn { get; init; }
    public required IReadOnlyList<string> ArtifactIds { get; init; }
    public required IReadOnlyList<string> ValidationIds { get; init; }
}
