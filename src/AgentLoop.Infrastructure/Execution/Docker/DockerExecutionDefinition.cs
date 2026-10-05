
namespace AgentLoop.Kernel.Projects;

public sealed record DockerExecutionDefinition(string Executable, string Image, string AssetRoot,
    IReadOnlyList<string> EntryPoint, string ProjectMount, string AssetMount,
    string User, string Memory, string Cpus, int Pids, string TemporaryMount, string WorkingDirectory,
    IReadOnlyDictionary<string, string> Environment, IReadOnlyDictionary<string, string> ResourceHashes,
    int ControlTimeoutSeconds, int CleanupTimeoutSeconds);
