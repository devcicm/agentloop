
namespace AgentLoop.Kernel.Projects;


public sealed record ProjectCommandResult(int ExitCode, string Output, bool CleanupConfirmed);
