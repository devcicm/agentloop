namespace AgentLoop.Kernel.Projects;

public sealed record ProjectCommandResult(int ExitCode, string Output, bool CleanupConfirmed);

public interface IProjectExecutionEnvironment
{
    int Running { get; }
    string Identity { get; }
    Task CheckPreviousRunAsync(Guid runId, CancellationToken token);
    Task PreflightAsync(CancellationToken token);
    Task<ProjectCommandResult> RunAsync(string commandId, TimeSpan timeout, CancellationToken token);
}

public interface IProjectEnvironmentFactory
{
    IProjectExecutionEnvironment Create(string root, Guid runId, Action<ProjectProcessNotice>? observer = null);
}

public sealed record ProjectProfileDto(
    int Version, string Id, string Instructions,
    IReadOnlyList<ProjectValidatorDefinition> Validators,
    IReadOnlyList<ProjectCommandDefinition> Commands,
    ProjectExecutionDefinition Execution, ProjectRuntimePolicy Policy);

public sealed record ProjectValidatorDefinition(string Id, string Handler, string? CommandId = null,
    string? CommandProfile = null, string? Scenario = null);
public sealed record ProjectCommandDefinition(string Id, IReadOnlyList<string> Arguments, bool ModelCallable,
    int TimeoutSeconds);
public sealed record ProjectExecutionDefinition(string Adapter, DockerExecutionDefinition? Docker = null,
    System.Text.Json.JsonElement? Settings = null);
public sealed record DockerExecutionDefinition(string Executable, string Image, string AssetRoot,
    IReadOnlyList<string> EntryPoint, string ProjectMount, string AssetMount,
    string User, string Memory, string Cpus, int Pids, string TemporaryMount, string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment, IReadOnlyDictionary<string, string> ResourceHashes,
    int ControlTimeoutSeconds, int CleanupTimeoutSeconds);
public sealed record ProjectRuntimePolicy(int MaxActionsWithoutProgress, int MaxExplorationReads,
    int MaxArtifactBytes, int MaxEvidenceBytes, int MaxObservationChars, int MaxSnapshotObservationChars,
    int MaxDefectChars, int MaxNoteChars, int EvidencePageChars, int MaxCommandOutputChars,
    long MaxJournalBytes, int MaxJournalEntryBytes, int ContextMargin,
    int StopDrainSeconds);

public interface ITokenBudgetEstimator
{
    string Version { get; }
    bool Exact { get; }
    int Estimate(string text);
}

public sealed record ProjectProcessNotice(string CommandId, string Resource, bool Started, int? ExitCode, bool? CleanupConfirmed);

public sealed record ProjectRecoveryPolicy(long MaxJournalBytes, int MaxEntryBytes);
