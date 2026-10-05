
namespace AgentLoop.Goals.Projects;

public sealed record ProjectKnowledgeSnapshot(IReadOnlyList<ArtifactKnowledge> Artifacts,
    int ActionsWithoutProgress, int ExplorationReads, IReadOnlyList<string>? ExplorationVersions = null);
