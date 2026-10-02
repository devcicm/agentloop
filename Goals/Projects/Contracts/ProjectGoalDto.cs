using System.Text.Json.Serialization;

namespace AgentLoop.Goals.Projects.Contracts;

// All fields are required unless explicitly nullable. Unknown fields are rejected at admission.
public sealed record ProjectGoalDto
{
    public required int Version { get; init; }
    public required string GoalId { get; init; }
    public required int Revision { get; init; }
    public required string Objective { get; init; }
    public required string Root { get; init; }
    public required IReadOnlyList<WorkItemDto> WorkItems { get; init; }
    public required IReadOnlyList<ArtifactSpecDto> Artifacts { get; init; }
    public required IReadOnlyList<ValidationSpecDto> Validations { get; init; }
    public required ProjectBudgetsDto Budgets { get; init; }
    public required string ExecutionProfile { get; init; }
}

public sealed record WorkItemDto
{
    public required string Id { get; init; }
    public required IReadOnlyList<string> DependsOn { get; init; }
    public required IReadOnlyList<string> ArtifactIds { get; init; }
    public required IReadOnlyList<string> ValidationIds { get; init; }
}

public sealed record ArtifactSpecDto
{
    public required string Id { get; init; }
    public required string RelativePath { get; init; }
    public required bool Required { get; init; }
    public required string ExpectedKind { get; init; }
    public required string ChangePolicy { get; init; }
}

public sealed record ValidationSpecDto
{
    public required string Id { get; init; }
    public required string ValidatorId { get; init; }
    public required string WorkItemId { get; init; }
    public required ValidationParametersDto Parameters { get; init; }
    public required bool Required { get; init; }
    public required int TimeoutSeconds { get; init; }
}

public sealed record ValidationParametersDto
{
    public required IReadOnlyList<string> ArtifactIds { get; init; }
    public string? CommandProfile { get; init; }
    public string? Scenario { get; init; }
}

public sealed record ProjectBudgetsDto
{
    public required int DeadlineSeconds { get; init; }
    public required int MaxModelCalls { get; init; }
    public required int MaxInputTokens { get; init; }
    public required int MaxOutputTokens { get; init; }
    public required int MaxCorrectionsPerWorkItem { get; init; }
    public required int MaxConcurrentProcesses { get; init; }
}

public static class ProjectJson
{
    public static System.Text.Json.JsonSerializerOptions Options => new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
    };
}
