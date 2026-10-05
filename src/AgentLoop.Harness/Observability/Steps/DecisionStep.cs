
namespace AgentLoop.Harness.Steps;


public sealed record DecisionStep(
    long Sequence,
    DateTimeOffset At,
    string Question,
    string Choice,
    string? Rationale) : Step(Sequence, At, "decision", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["question"] = Question,
            ["choice"] = Choice,
            ["rationale"] = Rationale,
        })
{
    public override StepKind Kind => StepKind.Decision;
}
