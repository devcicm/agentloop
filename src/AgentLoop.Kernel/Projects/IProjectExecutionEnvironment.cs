
namespace AgentLoop.Kernel.Projects;


public interface IProjectExecutionEnvironment
{
    int Running { get; }
    string Identity { get; }
    Task CheckPreviousRunAsync(Guid runId, CancellationToken token);
    Task PreflightAsync(CancellationToken token);
    Task<ProjectCommandResult> RunAsync(string commandId, TimeSpan timeout, CancellationToken token);
}
