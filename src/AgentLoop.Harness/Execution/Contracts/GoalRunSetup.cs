namespace AgentLoop.Harness.Composition;
public sealed class GoalRunSetup
{
    public required EventBus Bus { get; init; }
    public required IProcessSupervisor Supervisor { get; init; }
    public required CardLedger Ledger { get; init; }
    public required ToolRegistry Tools { get; init; }
    public required GoalContext GoalContext { get; init; }
    public required StepRecorder StepRecorder { get; init; }
    public required MetricsCollector Metrics { get; init; }
    public required GoalRunner Runner { get; init; }
    public required RunContext RunContext { get; init; }
}
