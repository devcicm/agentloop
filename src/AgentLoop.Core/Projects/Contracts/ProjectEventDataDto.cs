
namespace AgentLoop.Goals.Projects.Contracts;

public sealed record ProjectEventDataDto(int SchemaVersion, string ContractHash, string WorkItemId, string AttemptId);
