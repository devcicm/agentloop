namespace AgentLoop.Harness.Execution;

public sealed class ArtifactProjectEnvironment : IProjectExecutionEnvironment
{
    public int Running => 0;
    public string Identity => "artifact-host";
    public Task CheckPreviousRunAsync(Guid runId, CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task PreflightAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    public Task<ProjectCommandResult> RunAsync(string commandId, TimeSpan timeout, CancellationToken token)
        => throw new InvalidOperationException("Este adaptador no admite comandos.");
}
