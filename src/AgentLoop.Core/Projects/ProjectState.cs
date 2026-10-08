using AgentLoop.Graphs;
namespace AgentLoop.Goals.Projects;


/// <summary>Mutated only by the operation mailbox; dependency scheduling is triggered by events.</summary>
public sealed class ProjectState(ProjectGoalDto contract)
{
    private readonly Dictionary<string, WorkStateDto> _work = contract.WorkItems.ToDictionary(w => w.Id,
        w => new WorkStateDto(w.Id, ProjectWorkStatus.Pending, 0, ""), StringComparer.Ordinal);
    private readonly DirectedGraph _graph = BuildGraph(contract);
    public DirectedGraph DependencyGraph => _graph;
    private static DirectedGraph BuildGraph(ProjectGoalDto contract)
    {
        var graph = new DirectedGraph(Math.Max(1, contract.WorkItems.Count));
        foreach (var work in contract.WorkItems) graph.AddNode(new(work.Id, "work", contract.GoalId, contract.Revision.ToString()));
        foreach (var work in contract.WorkItems) foreach (var dependency in work.DependsOn) graph.AddEdge(new(dependency,work.Id,"requires"));
        if (!graph.TopologicalOrder().IsOk) throw new InvalidOperationException("Ciclo de dependencias.");
        return graph;
    }
    public IReadOnlyList<WorkStateDto> Snapshot => _work.Values.ToArray();
    public WorkStateDto? Active => _work.Values.SingleOrDefault(w => w.Status == ProjectWorkStatus.Active);
    public bool AllAccepted => _work.Values.All(w => w.Status == ProjectWorkStatus.Accepted);

    public WorkStateDto? EnableNext()
    {
        if (Active is not null || AllAccepted) return Active;
        var order = _graph.TopologicalOrder().Value!;
        var remaining = new Dictionary<string,double>();
        foreach (var id in order.Reverse())
        {
            var cost = contract.WorkItems.Single(w => w.Id == id).EstimatedSeconds ?? 0;
            remaining[id] = cost + _graph.Edges.Where(e => e.From == id).Select(e => remaining[e.To]).DefaultIfEmpty(0).Max();
        }
        var next = contract.WorkItems.OrderByDescending(w => remaining[w.Id]).FirstOrDefault(w => _work[w.Id].Status is ProjectWorkStatus.Pending or ProjectWorkStatus.Enabled
            && w.DependsOn.All(id => _work[id].Status == ProjectWorkStatus.Accepted));
        if (next is null) throw new InvalidOperationException("No hay trabajo habilitable; revisar dependencias o reconciliacion.");
        var old = _work[next.Id];
        if (old.Corrections > contract.Budgets.MaxCorrectionsPerWorkItem) throw new InvalidOperationException("Presupuesto de correcciones ya agotado para " + next.Id);
        if (old.Attempts >= 1 + contract.Budgets.MaxCorrectionsPerWorkItem)
            throw new InvalidOperationException("Presupuesto de intentos agotado para " + next.Id);
        return _work[next.Id] = old with { Status = ProjectWorkStatus.Active, Attempts = old.Attempts + 1, AttemptId = Guid.NewGuid().ToString("N") };
    }

    public void RecordValidationFailure(string? workItemId = null)
    {
        var active = workItemId is null ? Active : _work.GetValueOrDefault(workItemId);
        if (active is null) return;
        var corrections = active.Corrections + 1;
        _work[active.Id] = active with { Corrections = corrections };
        if (corrections > contract.Budgets.MaxCorrectionsPerWorkItem)
            throw new InvalidOperationException("Presupuesto de correcciones agotado para " + active.Id);
    }

    public void AcceptCurrent() => SetCurrent(ProjectWorkStatus.Accepted);
    public void AbortCurrent() => SetCurrent(ProjectWorkStatus.Pending);
    private void SetCurrent(ProjectWorkStatus status)
    {
        var active = Active ?? throw new InvalidOperationException("No hay tarea activa.");
        _work[active.Id] = active with { Status = status };
    }

    public void Invalidate(IEnumerable<string> artifactIds)
    {
        var invalid = contract.WorkItems.Where(w => w.ArtifactIds.Intersect(artifactIds).Any()).Select(w => w.Id).ToHashSet();
        foreach (var root in invalid.ToArray()) invalid.UnionWith(_graph.Descendants(root));
        foreach (var id in invalid)
            if (_work[id].Status == ProjectWorkStatus.Accepted) _work[id] = _work[id] with { Status = ProjectWorkStatus.Pending };
    }

    public void Restore(IReadOnlyList<WorkStateDto> states)
    {
        if (states is null || states.Count != _work.Count || states.Any(w => w is null) || states.Select(w => w.Id).Distinct().Count() != _work.Count)
            throw new InvalidOperationException("Snapshot con tareas inconsistentes.");
        foreach (var state in states)
        {
            if (state.Id is null || !_work.ContainsKey(state.Id) || !Enum.IsDefined(state.Status) || state.Attempts < 0
                || state.Attempts > 0 && !Guid.TryParseExact(state.AttemptId, "N", out _)
                || state.Attempts > 1 + contract.Budgets.MaxCorrectionsPerWorkItem || state.Corrections < 0
                || state.Corrections > 1 + contract.Budgets.MaxCorrectionsPerWorkItem)
                throw new InvalidOperationException("Snapshot de tarea invalido.");
            _work[state.Id] = state with { Status = state.Status == ProjectWorkStatus.Accepted ? ProjectWorkStatus.Accepted : ProjectWorkStatus.Pending };
        }
    }
}
