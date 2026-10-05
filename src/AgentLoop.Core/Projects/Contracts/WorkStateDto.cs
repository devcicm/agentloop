
namespace AgentLoop.Goals.Projects.Contracts;

public sealed record WorkStateDto(string Id, ProjectWorkStatus Status, int Attempts, string AttemptId, int Corrections = 0);
