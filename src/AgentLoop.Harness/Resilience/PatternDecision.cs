
namespace AgentLoop.Harness.Patterns;


/// <summary>
/// Decision de una Pattern. Continue deja pasar el intento actual;
/// Retry pide otro intento con el tiempo de espera indicado;
/// Fail corta la operacion con un motivo.
/// </summary>
public sealed record PatternDecision(PatternAction Action, TimeSpan RetryAfter, string? Reason)
{
    public static PatternDecision Continue()
        => new(PatternAction.Continue, TimeSpan.Zero, null);

    public static PatternDecision Retry(TimeSpan after, string? reason = null)
        => new(PatternAction.Retry, after, reason);

    public static PatternDecision Fail(string reason)
        => new(PatternAction.Fail, TimeSpan.Zero, reason);
}
