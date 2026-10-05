
namespace AgentLoop.Kernel.Projects;


public sealed record ProjectProfileDto(
    int Version, string Id, string Instructions,
    IReadOnlyList<ProjectValidatorDefinition> Validators,
    IReadOnlyList<ProjectCommandDefinition> Commands,
    ProjectExecutionDefinition Execution, ProjectRuntimePolicy Policy);
