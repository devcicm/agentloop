
namespace AgentLoop.Kernel.Projects;


public interface IProjectEnvironmentFactory
{
    IReadOnlyList<string> ResourcePaths => [];
    IProjectExecutionEnvironment Create(string root, Guid runId, Action<ProjectProcessNotice>? observer = null);
}
