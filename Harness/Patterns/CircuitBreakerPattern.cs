namespace AgentLoop.Harness.Patterns;

/// <summary>
/// Corta si se acumulan <see cref="Threshold"/> fallos consecutivos sin un
/// exito entre medias. Pensado para parar el bucle cuando un proveedor se
/// cae de verdad, en lugar de gastar reintentos.
/// </summary>
public sealed class CircuitBreakerPattern : IPattern
{
    public CircuitBreakerPattern(int threshold = 3)
    {
        if (threshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "Debe ser >= 1.");
        }

        Threshold = threshold;
    }

    public string Name => "circuit-breaker";

    public int Threshold { get; }

    public PatternDecision Apply(PatternContext context)
    {
        if (context.LastError is null)
        {
            return PatternDecision.Continue();
        }

        return PatternDecision.Fail($"circuit breaker: {Threshold} fallos consecutivos, ultimo: {context.LastError.Message}");
    }
}
