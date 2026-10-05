
namespace AgentLoop.Harness.Patterns;


/// <summary>
/// Recorta cualquier espera al <see cref="MaxTimeout"/>. Es el equivalente
/// declarativo del <c>ExecutionPolicy.Clamp</c>: si el caller pidio 3 horas
/// pero el policy dice maximo 10 minutos, aqui se aplica.
/// </summary>
public sealed class TimeoutPattern : IPattern
{
    public TimeoutPattern(TimeSpan maxTimeout)
    {
        if (maxTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxTimeout), "Debe ser positivo.");
        }

        MaxTimeout = maxTimeout;
    }

    public string Name => "timeout";

    public TimeSpan MaxTimeout { get; }

    public PatternDecision Apply(PatternContext context)
    {
        if (context.Elapsed > MaxTimeout)
        {
            return PatternDecision.Fail($"tiempo agotado: {context.Elapsed.TotalSeconds:0}s > {MaxTimeout.TotalSeconds:0}s");
        }

        return PatternDecision.Continue();
    }
}
