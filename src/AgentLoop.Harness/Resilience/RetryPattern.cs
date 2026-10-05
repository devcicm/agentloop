
namespace AgentLoop.Harness.Patterns;


/// <summary>
/// Reintenta con backoff exponencial hasta <see cref="MaxAttempts"/> veces.
/// El primer reintento espera <paramref name="baseDelay"/>; cada uno duplica.
/// Si se supera el maximo, devuelve Fail con el motivo original.
///
/// Es el patron que ya usa implicitamente el reactor para los 5xx/429, pero
/// aqui esta formalizado y separado para poder reutilizarlo desde cualquier
/// otra pieza del harness (cliente HTTP, IO a archivos, etc.).
/// </summary>
public sealed class RetryPattern : IPattern
{
    public RetryPattern(int maxAttempts = 3, TimeSpan baseDelay = default, TimeSpan? maxDelay = null)
    {
        if (maxAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Debe ser >= 1.");
        }

        MaxAttempts = maxAttempts;
        BaseDelay = baseDelay == default ? TimeSpan.FromSeconds(3) : baseDelay;
        MaxDelay = maxDelay ?? TimeSpan.FromSeconds(15);
    }

    public string Name => "retry";

    public int MaxAttempts { get; }

    public TimeSpan BaseDelay { get; }

    public TimeSpan MaxDelay { get; }

    public PatternDecision Apply(PatternContext context)
    {
        if (context.LastError is null || context.Attempt >= MaxAttempts)
        {
            return context.LastError is null
                ? PatternDecision.Continue()
                : PatternDecision.Fail($"reintentos agotados ({context.Attempt}/{MaxAttempts}): {context.LastError.Message}");
        }

        var seconds = BaseDelay.TotalSeconds * Math.Pow(2, context.Attempt - 1);
        var delay = TimeSpan.FromSeconds(Math.Min(seconds, MaxDelay.TotalSeconds));
        return PatternDecision.Retry(delay, $"intento {context.Attempt} de {MaxAttempts}, espera {delay.TotalSeconds:0}s");
    }
}
