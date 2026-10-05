using System.Text.Json.Serialization;

namespace AgentLoop.Goals.Projects.Contracts;


public sealed record ValidationSpecDto
{
    public required string Id { get; init; }
    public required string ValidatorId { get; init; }
    public required string WorkItemId { get; init; }
    public required ValidationParametersDto Parameters { get; init; }
    public required bool Required { get; init; }
    public required int TimeoutSeconds { get; init; }
}
