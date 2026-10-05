
namespace AgentLoop.Harness.Steps;


public sealed record WaitStep(
    long Sequence,
    DateTimeOffset At,
    string Reason,
    TimeSpan Slept,
    bool Woken) : Step(Sequence, At, "wait", Slept,
        new Dictionary<string, object?>
        {
            ["reason"] = Reason,
            ["slept_ms"] = (long)Slept.TotalMilliseconds,
            ["woken"] = Woken,
        })
{
    public override StepKind Kind => StepKind.Wait;
}
