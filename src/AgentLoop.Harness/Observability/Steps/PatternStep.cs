
namespace AgentLoop.Harness.Steps;


public sealed record PatternStep(
    long Sequence,
    DateTimeOffset At,
    string Pattern,
    string Decision,
    string? Reason) : Step(Sequence, At, $"pattern:{Pattern}", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["pattern"] = Pattern,
            ["decision"] = Decision,
            ["reason"] = Reason,
        })
{
    public override StepKind Kind => StepKind.Pattern;
}
