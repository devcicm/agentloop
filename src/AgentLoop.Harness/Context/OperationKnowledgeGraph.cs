using System.Text.Json;
using AgentLoop.Graphs;

namespace AgentLoop.Goals;

/// <summary>Proyeccion de hechos del host; nunca incorpora aristas propuestas por el LLM como hechos verificados.</summary>
internal sealed class OperationKnowledgeGraph
{
    public DirectedGraph Graph { get; }
    private readonly string _goal;
    public OperationKnowledgeGraph(string goal, int capacity)
    {
        _goal="goal:"+goal; Graph=new(capacity); Graph.AddNode(new(_goal,"goal","operator",goal));
    }
    public void Observe(ToolExecution execution, string? receipt)
    {
        var id="action:"+execution.ToolCallId;
        Graph.AddNode(new(id,execution.Failed ? "failed-action" : "action","host",execution.ToolCallId,execution.ToolName));
        Graph.AddEdge(new(_goal,id,"observed-action"));
        if (receipt is null) return;
        var evidence="evidence:"+receipt; Graph.AddNode(new(evidence,"evidence","host",receipt)); Graph.AddEdge(new(id,evidence,"produces"));
        using var data=JsonDocument.Parse(execution.Output);
        if (!data.RootElement.TryGetProperty("path",out var path) || path.ValueKind!=JsonValueKind.String) return;
        var resource="resource:"+path.GetString();
        var hash=data.RootElement.TryGetProperty("new_hash",out var changed) ? changed.GetString()
            : data.RootElement.TryGetProperty("hash",out var read) ? read.GetString() : null;
        if (hash is null) return;
        // Una nueva version invalida las relaciones antiguas como evidencia de la version actual.
        var nodeId=resource+"@"+hash;
        Graph.AddNode(new(nodeId,"resource-version",path.GetString()!,hash)); Graph.AddEdge(new(evidence,nodeId,"observes"));
    }
    public string Brief(int maxCharacters)
    {
        var count=Math.Min(Graph.Nodes.Count,24);
        while (count>=0)
        {
            var nodes=Graph.Nodes.TakeLast(count).ToArray(); var ids=nodes.Select(n=>n.Id).ToHashSet();
            var text=JsonSerializer.Serialize(new { nodes,edges=Graph.Edges.Where(e=>ids.Contains(e.From)&&ids.Contains(e.To)).TakeLast(32),partial=nodes.Length<Graph.Nodes.Count });
            if (text.Length<=maxCharacters) return text;
            count--;
        }
        return "{\"partial\":true}";
    }
}
