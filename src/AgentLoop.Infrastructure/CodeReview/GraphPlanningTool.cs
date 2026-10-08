using System.Text.Json;
using AgentLoop.Graphs;

namespace AgentLoop.Tools;

public sealed record GraphAnalysisRequest(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges,
    IReadOnlyDictionary<string,double>? Durations = null, IReadOnlyDictionary<string,double>? Heuristic = null,
    IReadOnlyDictionary<string,IReadOnlyList<AndOrAlternative>>? Alternatives = null, IReadOnlyList<string>? Satisfied = null);

public static class GraphPlanningTool
{
    public static ITool Create() => new DelegateTool("graph_plan",
        "Analiza un grafo declarado: topologia, componentes, ruta critica, shortest/A*, cut o and_or. Es una recomendacion matematica, no permiso ni evidencia de efectos. astar usa heuristica declarada; no garantiza optimalidad si sobreestima.",
        """{"type":"object","properties":{"path":{"type":"string"},"algorithm":{"type":"string","enum":["topological","components","critical","shortest","astar","cut","and_or"]},"source":{"type":"string"},"target":{"type":"string"}},"required":["path","algorithm"]}""",
        AnalyzeAsync, new(ToolEffect.None));
    private static async ValueTask<string> AnalyzeAsync(JsonElement args,CancellationToken token)
    {
        var path=ToolArguments.ResolvePath(args,"path"); ProjectContractAdmission.CheckResource(path);
        if (new FileInfo(path).Length > 256000) throw new ArgumentException("Grafo fuera de presupuesto.");
        var data=JsonSerializer.Deserialize<GraphAnalysisRequest>(StrictJson.Parse(await Task.Run(() => BoundedFile.ReadText(path,256000),token),256000).GetRawText())
            ?? throw new ArgumentException("Grafo vacio.");
        if (data.Nodes is null || data.Edges is null || data.Nodes.Count > 256 || data.Edges.Count > 4096) throw new ArgumentException("Grafo fuera de presupuesto.");
        var graph=new DirectedGraph(256); foreach(var node in data.Nodes) graph.AddNode(node); foreach(var edge in data.Edges) graph.AddEdge(edge);
        var source=ToolArguments.ReadString(args,"source") ?? ""; var target=ToolArguments.ReadString(args,"target") ?? "";
        object result=ToolArguments.ReadRequiredString(args,"algorithm") switch
        {
            "topological" => graph.TopologicalOrder(), "components" => graph.StrongComponents(),
            "critical" => graph.CriticalPath(n => data.Durations?.GetValueOrDefault(n.Id) ?? 0),
            "shortest" => graph.ShortestPath(source,target),
            "astar" => GraphPlanning.AStar(graph,source,target,n => data.Heuristic?.GetValueOrDefault(n) ?? 0),
            "cut" => GraphPlanning.MinimumCut(graph,source,target,e => e.Cost),
            "and_or" => GraphPlanning.ResolveAndOr(target,data.Alternatives ?? new Dictionary<string,IReadOnlyList<AndOrAlternative>>(),(data.Satisfied ?? []).ToHashSet()),
            _ => throw new ArgumentException("Algoritmo no soportado.")
        };
        Failure? failure=result switch
        {
            Result<GraphPath,Failure> pathResult when !pathResult.IsOk => pathResult.Error,
            Result<GraphCut,Failure> cut when !cut.IsOk => cut.Error,
            Result<AndOrPlan,Failure> plan when !plan.IsOk => plan.Error,
            Result<IReadOnlyList<string>,Failure> order when !order.IsOk => order.Error,
            _ => null
        };
        return failure is not null ? JsonSerializer.Serialize(new { error=failure,recommendation=true,admitted=false })
            : JsonSerializer.Serialize(new { recommendation=true,admitted=false,result });
    }
}
