
namespace AgentLoop.Kernel.Projects;


public sealed record ProjectValidatorDefinition(string Id, string Handler, string? CommandId = null,
    string? CommandProfile = null, string? Scenario = null);
