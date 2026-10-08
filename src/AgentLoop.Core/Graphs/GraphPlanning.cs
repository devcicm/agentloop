using AgentLoop.Harness.Functional;

namespace AgentLoop.Graphs;

public sealed record GraphCut(double Capacity, IReadOnlyList<string> Reachable, IReadOnlyList<GraphEdge> CutEdges);
public sealed record AndOrAlternative(string Id, IReadOnlyList<string> Requires, double Cost, bool Allowed = true);
public sealed record AndOrPlan(IReadOnlyList<string> Alternatives, double EstimatedCost);

public static class GraphPlanning
{
    public static Result<GraphPath, Failure> AStar(DirectedGraph graph, string source, string target,
        Func<string, double> heuristic, Func<GraphEdge, bool>? allowed = null)
    {
        var correlation = Guid.NewGuid().ToString("N");
        if (!graph.Nodes.Any(n => n.Id == source) || !graph.Nodes.Any(n => n.Id == target)) return Fail<GraphPath>("graph.node_missing", correlation);
        var edges = graph.Edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToArray());
        var costs = new Dictionary<string,double> { [source] = 0 }; var parents = new Dictionary<string,string>();
        double H(string id) { var value = heuristic(id); if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Heuristica invalida."); return value; }
        var queue = new PriorityQueue<(string Id, double Cost),double>(); queue.Enqueue((source,0),H(source));
        while (queue.TryDequeue(out var item,out _))
        {
            if (item.Cost > costs[item.Id]) continue;
            if (item.Id == target)
            {
                var path = new List<string> { target }; var id = target;
                while (parents.TryGetValue(id,out var parent)) { path.Add(parent); id=parent; }
                path.Reverse(); return Result<GraphPath,Failure>.Ok(new(path,item.Cost));
            }
            foreach (var edge in edges.GetValueOrDefault(item.Id) ?? [])
            {
                if (!edge.Verified || !(allowed?.Invoke(edge) ?? true)) continue;
                var cost = item.Cost + edge.Cost;
                if (cost >= costs.GetValueOrDefault(edge.To,double.PositiveInfinity)) continue;
                costs[edge.To]=cost; parents[edge.To]=item.Id; queue.Enqueue((edge.To,cost),cost+H(edge.To));
            }
        }
        return Fail<GraphPath>("graph.no_path", correlation);
    }

    /// <summary>Edmonds-Karp; capacidades explicitas no equivalen a permisos o aislamiento operativo.</summary>
    public static Result<GraphCut, Failure> MinimumCut(DirectedGraph graph, string source, string target, Func<GraphEdge,double> capacity)
    {
        var correlation = Guid.NewGuid().ToString("N");
        if (source == target || !graph.Nodes.Any(n => n.Id == source) || !graph.Nodes.Any(n => n.Id == target)) return Fail<GraphCut>("graph.node_missing",correlation);
        var residual = graph.Nodes.ToDictionary(n => n.Id,_ => new Dictionary<string,double>());
        var original = new Dictionary<GraphEdge,double>();
        foreach (var edge in graph.Edges)
        {
            var value = capacity(edge); if (!double.IsFinite(value) || value < 0) throw new ArgumentException("Capacidad invalida.");
            original[edge]=value;
            residual[edge.From][edge.To]=residual[edge.From].GetValueOrDefault(edge.To)+value;
            residual[edge.To].TryAdd(edge.From,0);
        }
        double flow=0;
        while (true)
        {
            var parents=new Dictionary<string,string>(); var seen=new HashSet<string> {source}; var queue=new Queue<string>(); queue.Enqueue(source);
            while (queue.TryDequeue(out var id) && !seen.Contains(target))
                foreach (var pair in residual[id]) if (pair.Value > 0 && seen.Add(pair.Key)) { parents[pair.Key]=id; queue.Enqueue(pair.Key); }
            if (!seen.Contains(target))
                return Result<GraphCut,Failure>.Ok(new(flow,seen.ToArray(),original.Keys.Where(e => seen.Contains(e.From) && !seen.Contains(e.To)).ToArray()));
            var amount=double.PositiveInfinity; var cursor=target;
            while (cursor!=source) { var parent=parents[cursor]; amount=Math.Min(amount,residual[parent][cursor]); cursor=parent; }
            cursor=target;
            while (cursor!=source) { var parent=parents[cursor]; residual[parent][cursor]-=amount; residual[cursor][parent]=residual[cursor].GetValueOrDefault(parent)+amount; cursor=parent; }
            flow+=amount;
        }
    }

    /// <summary>Plan factible AND/OR acotado; costes de subobjetivos compartidos pueden sobreestimarse.</summary>
    public static Result<AndOrPlan,Failure> ResolveAndOr(string goal, IReadOnlyDictionary<string,IReadOnlyList<AndOrAlternative>> alternatives,
        IReadOnlySet<string> satisfied)
    {
        var correlation=Guid.NewGuid().ToString("N");
        var expanded=0;
        Result<AndOrPlan,Failure> Resolve(string id,HashSet<string> visiting)
        {
            if (++expanded > 10000) return Fail<AndOrPlan>("graph.search_budget",correlation);
            if (satisfied.Contains(id)) return Result<AndOrPlan,Failure>.Ok(new([],0));
            if (!visiting.Add(id) || visiting.Count > 256) return Fail<AndOrPlan>("graph.cycle_or_depth",correlation);
            AndOrPlan? best=null;
            foreach (var alternative in alternatives.GetValueOrDefault(id) ?? [])
            {
                if (!alternative.Allowed) continue;
                if (!double.IsFinite(alternative.Cost) || alternative.Cost < 0) throw new ArgumentException("Coste de alternativa invalido.");
                var chosen=new List<string> {alternative.Id}; var cost=alternative.Cost; var valid=true;
                foreach (var requirement in alternative.Requires)
                {
                    var child=Resolve(requirement,new(visiting));
                    if (!child.IsOk) { valid=false; break; }
                    chosen.AddRange(child.Value!.Alternatives); cost+=child.Value.EstimatedCost;
                }
                if (valid && (best is null || cost < best.EstimatedCost)) best=new(chosen.Distinct().ToArray(),cost);
            }
            return best is null ? Fail<AndOrPlan>("graph.no_plan",correlation) : Result<AndOrPlan,Failure>.Ok(best);
        }
        return Resolve(goal,[]);
    }
    private static Result<T,Failure> Fail<T>(string code,string correlation) => Result<T,Failure>.Fail(new(code,"No existe una solucion valida para las precondiciones del grafo.",correlation));
}
