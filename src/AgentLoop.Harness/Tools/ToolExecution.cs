using System.Text.Json;
namespace AgentLoop.Tools;


/// <summary>Resultado de ejecutar una tool call, listo para volver al modelo.</summary>
public sealed record ToolExecution(string ToolCallId, string ToolName, string Output, bool Failed)
{
    public bool EffectsMayHaveOccurred { get; init; }
}
