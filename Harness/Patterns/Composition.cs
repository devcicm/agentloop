namespace AgentLoop.Harness.Patterns;

/// <summary>
/// Compone varias Patterns en una sola decision. Las que devuelven Fail
/// ganan; si hay varios Retry, gana el que pida mas espera; en otro caso
/// Continue.
///
/// El orden importa: si Timeout va antes que Retry, un intento que ya
/// excedio el tiempo se rechaza antes de gastar el reintento. En general
/// se pone Timeout al principio y Retry al final.
/// </summary>
public sealed class Composition : IPattern
{
    private readonly IReadOnlyList<IPattern> _patterns;

    public Composition(string name, params IPattern[] patterns)
    {
        if (patterns is null || patterns.Length == 0)
        {
            throw new ArgumentException("Al menos una Pattern.", nameof(patterns));
        }

        Name = name;
        _patterns = patterns;
    }

    public string Name { get; }

    public PatternDecision Apply(PatternContext context)
    {
        TimeSpan maxRetry = TimeSpan.Zero;
        string? retryReason = null;

        foreach (var pattern in _patterns)
        {
            var decision = pattern.Apply(context);
            switch (decision.Action)
            {
                case PatternAction.Fail:
                    return decision;
                case PatternAction.Retry:
                    if (decision.RetryAfter > maxRetry)
                    {
                        maxRetry = decision.RetryAfter;
                        retryReason = decision.Reason;
                    }
                    break;
                case PatternAction.Continue:
                    break;
            }
        }

        return maxRetry > TimeSpan.Zero
            ? PatternDecision.Retry(maxRetry, retryReason)
            : PatternDecision.Continue();
    }
}
