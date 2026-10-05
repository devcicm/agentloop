using System.Text.Json.Serialization;

namespace AgentLoop.Goals.Projects.Contracts;


public sealed record ArtifactSpecDto
{
    public required string Id { get; init; }
    public required string RelativePath { get; init; }
    public required bool Required { get; init; }
    public required string ExpectedKind { get; init; }
    public required string ChangePolicy { get; init; }
}
