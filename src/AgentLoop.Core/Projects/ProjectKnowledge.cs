
namespace AgentLoop.Goals.Projects;


/// <summary>Task knowledge is a bounded projection of events, independent of files, model and transport.</summary>
public sealed class ProjectKnowledge
{
    private readonly Dictionary<string, ArtifactKnowledge> _artifacts = new(StringComparer.Ordinal);
    private readonly HashSet<string> _exploredVersions = new(StringComparer.Ordinal);
    public int ActionsWithoutProgress { get; private set; }
    public int ExplorationReads { get; private set; }
    public IReadOnlyList<ArtifactKnowledge> Artifacts => _artifacts.Values.ToArray();

    public bool ObserveRead(string artifactId, string hash, int explorationLimit)
    {
        var fresh = !_artifacts.TryGetValue(artifactId, out var previous) || previous.Hash != hash;
        _artifacts[artifactId] = new(artifactId, hash, fresh ? 1 : previous!.Reads + 1,
            fresh ? null : previous!.Note);
        var key = artifactId + ":" + hash;
        if (ExplorationReads < explorationLimit && _exploredVersions.Add(key)) { ExplorationReads++; return true; }
        return false;
    }
    public void Note(string artifactId, string hash, string note)
    {
        _artifacts.TryGetValue(artifactId, out var previous);
        _artifacts[artifactId] = new(artifactId, hash, previous?.Hash == hash ? previous.Reads : 0, note);
    }
    public void RecordAction(bool materialProgress, bool boundedExploration)
    {
        if (materialProgress) ActionsWithoutProgress = 0;
        else if (!boundedExploration) ActionsWithoutProgress++;
    }
    public void BeginWork() { ActionsWithoutProgress = 0; ExplorationReads = 0; _exploredVersions.Clear(); }
    public ProjectKnowledgeSnapshot Snapshot() => new(Artifacts, ActionsWithoutProgress, ExplorationReads, _exploredVersions.ToArray());
    public void Restore(ProjectKnowledgeSnapshot snapshot, IReadOnlySet<string> knownIds)
    {
        if (snapshot.Artifacts is null || snapshot.Artifacts.Count > knownIds.Count
            || snapshot.ActionsWithoutProgress < 0 || snapshot.ExplorationReads < 0)
            throw new InvalidOperationException("Estado de contexto incompatible.");
        _artifacts.Clear();
        foreach (var item in snapshot.Artifacts)
            if (item is null || !knownIds.Contains(item.ArtifactId) || item.Reads < 0 || string.IsNullOrEmpty(item.Hash)
                || !_artifacts.TryAdd(item.ArtifactId, item)) throw new InvalidOperationException("Observacion de contexto incompatible.");
        ActionsWithoutProgress = snapshot.ActionsWithoutProgress;
        ExplorationReads = snapshot.ExplorationReads;
        _exploredVersions.Clear();
        foreach (var key in snapshot.ExplorationVersions ?? [])
        {
            var separator = key?.IndexOf(':') ?? -1;
            if (separator < 1 || !knownIds.Contains(key![..separator]) || !_exploredVersions.Add(key))
                throw new InvalidOperationException("Lectura de contexto incompatible.");
        }
        if (_exploredVersions.Count != snapshot.ExplorationReads)
            throw new InvalidOperationException("Presupuesto de exploracion incompatible.");
    }
}
