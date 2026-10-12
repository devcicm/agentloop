using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgentLoop.Harness.Observability;

/// <summary>Telemetría opcional y sin datos de prompts, rutas ni credenciales.</summary>
public static class HarnessTelemetry
{
    public static readonly ActivitySource Activities = new("AgentLoop.Harness", "0.1.0");
    private static readonly Meter Meter = new("AgentLoop.Harness", "0.1.0");
    private static readonly Counter<long> Operations = Meter.CreateCounter<long>("agentloop.operations");
    private static readonly Counter<long> ModelCalls = Meter.CreateCounter<long>("agentloop.model.calls");
    private static readonly Counter<long> ToolCalls = Meter.CreateCounter<long>("agentloop.tool.calls");

    public static void OperationStarted() => Operations.Add(1, new KeyValuePair<string, object?>("state", "started"));
    public static void ModelRequested() => ModelCalls.Add(1);
    public static void ToolCompleted(bool succeeded) => ToolCalls.Add(1, new KeyValuePair<string, object?>("outcome", succeeded ? "succeeded" : "failed"));
    public static void OperationEnded(OperationStatus status) => Operations.Add(1, new KeyValuePair<string, object?>("state", status.ToString()));
}
