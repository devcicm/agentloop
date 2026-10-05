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
