namespace AgentLoop.Harness.Patterns;

/// <summary>
/// Receta reutilizable que se aplica alrededor de una operacion. Una Pattern
/// recibe un <see cref="PatternContext"/> con el intento actual y devuelve la
/// decision: dejar pasar, esperar, reintentar o cortar.
///
/// Las Patterns no son middleware en el sentido de ASP.NET: cada una decide
/// sola en funcion del contexto, sin cadena fija. Composition las combina.
/// </summary>
public interface IPattern
{
    string Name { get; }

    PatternDecision Apply(PatternContext context);
}

/// <summary>
/// Estado minimo que una Pattern observa: numero de intento, ultimo error,
/// tiempo gastado, y si la operacion fue cancelada.
/// </summary>
public readonly record struct PatternContext(
    int Attempt,
    Exception? LastError,
    TimeSpan Elapsed,
    bool Cancelled);

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

public enum PatternAction
{
    Continue,
    Retry,
    Fail,
}
