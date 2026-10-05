
namespace AgentLoop.Harness.Steps;


public sealed record ToolStep(
    long Sequence,
    DateTimeOffset At,
    string Tool,
    string Arguments,
    string Output,
    bool Succeeded,
    TimeSpan Elapsed) : Step(Sequence, At, $"tool:{Tool}", Elapsed,
        new Dictionary<string, object?>
        {
            ["tool"] = Tool,
            ["arguments"] = Arguments,
            ["output"] = Output,
            ["succeeded"] = Succeeded,
        })
{
    public override StepKind Kind => StepKind.ToolCall;
}
