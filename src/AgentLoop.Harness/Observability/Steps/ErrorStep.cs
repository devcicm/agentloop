
namespace AgentLoop.Harness.Steps;


public sealed record ErrorStep(
    long Sequence,
    DateTimeOffset At,
    string Where,
    string Message,
    bool Transient) : Step(Sequence, At, $"error:{Where}", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["where"] = Where,
            ["message"] = Message,
            ["transient"] = Transient,
        })
{
    public override StepKind Kind => StepKind.Error;
}
