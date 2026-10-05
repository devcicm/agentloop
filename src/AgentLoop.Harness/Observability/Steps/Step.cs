
namespace AgentLoop.Harness.Steps;


/// <summary>
/// Unidad atomica de trabajo del harness. Cada vez que el reactor invoca al
/// modelo, despacha una tool, transiciona de estado, espera un evento o
/// detecta un error, se registra un Step con su Correlation para que el
/// Journey lo muestre en orden.
///
/// Se modela como jerarquia: Step base + subtipos concretos que aportan el
/// Data especifico. Asi un ToolCall lleva el nombre y los argumentos; un
/// ModelCall lleva el conteo de tokens; etc.
/// </summary>
public abstract record Step(
    long Sequence,
    DateTimeOffset At,
    string Name,
    TimeSpan Elapsed,
    System.Collections.Generic.IReadOnlyDictionary<string, object?> Data)
{
    public abstract StepKind Kind { get; }
}
