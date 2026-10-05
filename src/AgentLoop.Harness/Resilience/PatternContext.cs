
namespace AgentLoop.Harness.Patterns;


/// <summary>
/// Estado minimo que una Pattern observa: numero de intento, ultimo error,
/// tiempo gastado, y si la operacion fue cancelada.
/// </summary>
public readonly record struct PatternContext(
    int Attempt,
    Exception? LastError,
    TimeSpan Elapsed,
    bool Cancelled);
