namespace AgentLoop.Goals.Projects.Contracts;

public enum ProjectWorkStatus { Pending, Enabled, Active, Accepted, Failed, ReconciliationRequired }
public sealed record WorkStateDto(string Id, ProjectWorkStatus Status, int Attempts, string AttemptId, int Corrections = 0);
public sealed record ValidationResultDto(string ValidationId, string AttemptId, string ContractHash,
    IReadOnlyDictionary<string, string> InputHashes, bool Passed, string Observations,
    string EvidenceId, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string ValidatorVersion = "1", string ExecutionEnvironment = "artifact-host");
public sealed record ProjectSnapshotDto(int Version, string ContractHash, DateTimeOffset StartedAt,
    int ModelCalls, IReadOnlyList<WorkStateDto> Work, IReadOnlyList<ValidationResultDto> Results,
    IReadOnlyDictionary<string, string> Baseline);
public sealed record ProjectEventDataDto(int SchemaVersion, string ContractHash, string WorkItemId, string AttemptId);
