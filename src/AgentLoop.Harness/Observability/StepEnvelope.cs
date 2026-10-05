namespace AgentLoop.Harness.Observability;


/// <summary>Sobre minimo para que un Step se mueva por el bus de observers.</summary>
public sealed record StepEnvelope(
    Guid RunId,
    long Sequence,
    DateTimeOffset At,
    string Kind,
    CorrelationContext? Correlation,
    IReadOnlyDictionary<string, object?> Data);
