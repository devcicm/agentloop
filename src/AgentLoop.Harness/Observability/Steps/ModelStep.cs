
namespace AgentLoop.Harness.Steps;


public sealed record ModelStep(
    long Sequence,
    DateTimeOffset At,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    TimeSpan Elapsed,
    string? Thinking) : Step(Sequence, At, $"model:{Model}", Elapsed,
        new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["prompt_tokens"] = PromptTokens,
            ["completion_tokens"] = CompletionTokens,
            ["thinking"] = Thinking,
        })
{
    public override StepKind Kind => StepKind.ModelCall;
}
