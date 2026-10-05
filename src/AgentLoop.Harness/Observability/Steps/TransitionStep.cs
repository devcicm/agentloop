
namespace AgentLoop.Harness.Steps;


public sealed record TransitionStep(
    long Sequence,
    DateTimeOffset At,
    string From,
    string To,
    string? Reason) : Step(Sequence, At, "transition", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["from"] = From,
            ["to"] = To,
            ["reason"] = Reason,
        })
{
    public override StepKind Kind => StepKind.Transition;
}
