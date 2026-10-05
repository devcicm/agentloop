
namespace AgentLoop.Kernel.Projects;

public sealed record ProjectCommandDefinition(string Id, IReadOnlyList<string> Arguments, bool ModelCallable,
    int TimeoutSeconds);
