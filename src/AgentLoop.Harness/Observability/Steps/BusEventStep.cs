
namespace AgentLoop.Harness.Steps;


/// <summary>
/// Un AgentEvent del bus convertido en Step. Asi el Journey del comando
/// journal muestra todo lo que publico el reactor (cada ciclo de narracion,
/// cada aviso, cada tool call) ademas del TransitionStep final.
/// </summary>
public sealed record BusEventStep(
    long Sequence,
    DateTimeOffset At,
    long EventSequence,
    string EventKind,
    string Source,
    string Text,
    string? Payload) : Step(Sequence, At, $"bus:{EventKind}", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["event_sequence"] = EventSequence,
            ["kind"] = EventKind,
            ["source"] = Source,
            ["text"] = Text,
            ["data"] = Payload ?? string.Empty,
        })
{
    public override StepKind Kind => StepKind.BusEvent;
}
