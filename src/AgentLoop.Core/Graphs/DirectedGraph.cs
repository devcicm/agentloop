using AgentLoop.Harness.Functional;

namespace AgentLoop.Graphs;

public sealed record GraphNode(string Id, string Kind, string Source, string Revision, string? Label = null);
public sealed record GraphEdge(string From, string To, string Kind, double Cost = 1, bool Verified = true);
public sealed record GraphPath(IReadOnlyList<string> Nodes, double Cost);

/// <summary>Grafo dirigido acotado: topologia, impacto, componentes fuertes y rutas ponderadas.</summary>
public sealed class DirectedGraph(int capacity = 10000)
{
    private int _edgeCount;
    private readonly Dictionary<string, GraphNode> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<GraphEdge>> _edges = new(StringComparer.Ordinal);
    public IReadOnlyCollection<GraphNode> Nodes => _nodes.Values;
    public IReadOnlyList<GraphEdge> Edges => _edges.Values.SelectMany(x => x).ToArray();
    public void AddNode(GraphNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Id) || !_nodes.ContainsKey(node.Id) && _nodes.Count >= capacity)
            throw new ArgumentException("Nodo invalido o grafo fuera de presupuesto.");
        _nodes[node.Id] = node; _edges.TryAdd(node.Id, []);
    }
    public void AddEdge(GraphEdge edge)
    {
        if (!_nodes.ContainsKey(edge.From) || !_nodes.ContainsKey(edge.To) || !double.IsFinite(edge.Cost) || edge.Cost < 0)
            throw new ArgumentException("Arista invalida; requiere nodos existentes y coste no negativo.");
        if (!_edges[edge.From].Contains(edge))
        {
            if (_edgeCount >= capacity * 16L) throw new ArgumentException("Aristas fuera de presupuesto.");
            _edges[edge.From].Add(edge); _edgeCount++;
        }
    }
    public IReadOnlySet<string> Descendants(string id)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>(); pending.Enqueue(id);
        while (pending.TryDequeue(out var current))
            foreach (var edge in _edges.GetValueOrDefault(current) ?? [])
                if (visited.Add(edge.To)) pending.Enqueue(edge.To);
        visited.Remove(id); return visited;
    }
    public Result<IReadOnlyList<string>, Failure> TopologicalOrder()
    {
        var incoming = _nodes.Keys.ToDictionary(id => id, _ => 0);
        foreach (var edge in Edges) incoming[edge.To]++;
        var ready = new PriorityQueue<string, string>(StringComparer.Ordinal);
        foreach (var pair in incoming.Where(p => p.Value == 0)) ready.Enqueue(pair.Key, pair.Key);
        var result = new List<string>();
        while (ready.TryDequeue(out var id, out _))
        {
            result.Add(id);
            foreach (var edge in _edges[id]) if (--incoming[edge.To] == 0) ready.Enqueue(edge.To, edge.To);
        }
        return result.Count == _nodes.Count ? Result<IReadOnlyList<string>, Failure>.Ok(result)
            : Result<IReadOnlyList<string>, Failure>.Fail(new("graph.cycle", "Las dependencias contienen un ciclo.", Guid.NewGuid().ToString("N")));
    }
    // Kosaraju iterativo: evita agotar el stack en repositorios con cadenas largas.
    public IReadOnlyList<IReadOnlyList<string>> StrongComponents()
    {
        var visited = new HashSet<string>(); var order = new List<string>();
        foreach (var start in _nodes.Keys)
        {
            var stack = new Stack<(string Id, bool Exit)>(); stack.Push((start, false));
            while (stack.TryPop(out var item))
            {
                if (item.Exit) { order.Add(item.Id); continue; }
                if (!visited.Add(item.Id)) continue;
                stack.Push((item.Id, true));
                foreach (var edge in _edges[item.Id]) if (!visited.Contains(edge.To)) stack.Push((edge.To, false));
            }
        }
        var reverse = _nodes.Keys.ToDictionary(id => id, _ => new List<string>());
        foreach (var edge in Edges) reverse[edge.To].Add(edge.From);
        visited.Clear(); var components = new List<IReadOnlyList<string>>();
        foreach (var start in order.AsEnumerable().Reverse())
        {
            if (!visited.Add(start)) continue;
            var component = new List<string>(); var stack = new Stack<string>(); stack.Push(start);
            while (stack.TryPop(out var id)) { component.Add(id); foreach (var next in reverse[id]) if (visited.Add(next)) stack.Push(next); }
            components.Add(component);
        }
        return components;
    }
    public Result<GraphPath, Failure> ShortestPath(string source, string target, Func<GraphEdge, bool>? allowed = null)
    {
        var correlation = Guid.NewGuid().ToString("N");
        if (!_nodes.ContainsKey(source) || !_nodes.ContainsKey(target))
            return Result<GraphPath, Failure>.Fail(new("graph.node_missing", "Nodo de ruta inexistente.", correlation));
        var distance = new Dictionary<string, double> { [source] = 0 }; var parent = new Dictionary<string, string>();
        var queue = new PriorityQueue<string, double>(); queue.Enqueue(source, 0);
        while (queue.TryDequeue(out var id, out var cost))
        {
            if (cost > distance[id]) continue;
            if (id == target)
            {
                var path = new List<string> { id }; while (parent.TryGetValue(id, out var previous)) { path.Add(previous); id = previous; }
                path.Reverse(); return Result<GraphPath, Failure>.Ok(new(path, cost));
            }
            foreach (var edge in _edges[id].Where(e => e.Verified && (allowed?.Invoke(e) ?? true)))
            {
                var next = cost + edge.Cost;
                if (next >= distance.GetValueOrDefault(edge.To, double.PositiveInfinity)) continue;
                distance[edge.To] = next; parent[edge.To] = id; queue.Enqueue(edge.To, next);
            }
        }
        return Result<GraphPath, Failure>.Fail(new("graph.no_path", "No hay ruta admitida hasta el objetivo.", correlation));
    }
    public Result<GraphPath, Failure> CriticalPath(Func<GraphNode, double> duration)
    {
        var order = TopologicalOrder();
        if (!order.IsOk) return Result<GraphPath, Failure>.Fail(order.Error!);
        var costs = _nodes.Keys.ToDictionary(id => id, id => duration(_nodes[id]));
        if (costs.Values.Any(v => !double.IsFinite(v) || v < 0)) throw new ArgumentException("Duraciones invalidas.");
        var previous = new Dictionary<string, string>();
        foreach (var id in order.Value!) foreach (var edge in _edges[id])
        {
            var cost = costs[id] + duration(_nodes[edge.To]);
            if (cost > costs[edge.To]) { costs[edge.To] = cost; previous[edge.To] = id; }
        }
        if (costs.Count == 0) return Result<GraphPath, Failure>.Ok(new([], 0));
        var last = costs.MaxBy(p => p.Value); var cursor = last.Key; var path = new List<string> { cursor };
        while (previous.TryGetValue(cursor, out var parent)) { path.Add(parent); cursor = parent; }
        path.Reverse(); return Result<GraphPath, Failure>.Ok(new(path, last.Value));
    }
}
