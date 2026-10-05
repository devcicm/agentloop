using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
namespace AgentLoop.Harness.Execution;


/// <summary>Host-selected profiles only. No shell, no host env forwarded, read-only project mount, no external network.</summary>
public sealed class DockerEnvironmentFactory(DockerExecutionDefinition definition, IReadOnlyList<ProjectCommandDefinition> commands, ProjectRuntimePolicy policy) : IProjectEnvironmentFactory
{
    public IReadOnlyList<string> ResourcePaths => [definition.AssetRoot];
    public IProjectExecutionEnvironment Create(string root, Guid runId, Action<ProjectProcessNotice>? observer = null)
        => new DockerProjectEnvironment(root, runId, definition, commands, policy, observer);
}
