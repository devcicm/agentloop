
namespace AgentLoop.Kernel.Projects;


public sealed record ProjectProcessNotice(string CommandId, string Resource, bool Started, int? ExitCode, bool? CleanupConfirmed);
