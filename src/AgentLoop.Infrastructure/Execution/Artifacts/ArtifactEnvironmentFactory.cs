namespace AgentLoop.Harness.Execution;


public sealed class ArtifactEnvironmentFactory : IProjectEnvironmentFactory
{
    public IProjectExecutionEnvironment Create(string root, Guid runId, Action<ProjectProcessNotice>? observer = null) => new ArtifactProjectEnvironment();
}
