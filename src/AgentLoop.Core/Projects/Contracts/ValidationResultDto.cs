
namespace AgentLoop.Goals.Projects.Contracts;

public sealed record ValidationResultDto(string ValidationId, string AttemptId, string ContractHash,
    IReadOnlyDictionary<string, string> InputHashes, bool Passed, string Observations,
    string EvidenceId, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string ValidatorVersion = "1", string ExecutionEnvironment = "artifact-host", string? ProfileHash = null);
