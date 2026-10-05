
namespace AgentLoop.Goals.Projects.Contracts;

public sealed record ProjectSnapshotDto(int Version, string ContractHash, DateTimeOffset StartedAt,
    int ModelCalls, IReadOnlyList<WorkStateDto> Work, IReadOnlyList<ValidationResultDto> Results,
    IReadOnlyDictionary<string, string> Baseline, string? ProfileHash = null, ProjectKnowledgeSnapshot? Knowledge = null);
