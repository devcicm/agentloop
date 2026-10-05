using System.Text.Json.Serialization;

namespace AgentLoop.Goals.Projects.Contracts;


public sealed record ProjectBudgetsDto
{
    public required int DeadlineSeconds { get; init; }
    public required int MaxModelCalls { get; init; }
    public required int MaxInputTokens { get; init; }
    public required int MaxOutputTokens { get; init; }
    public required int MaxCorrectionsPerWorkItem { get; init; }
    public required int MaxConcurrentProcesses { get; init; }
}
