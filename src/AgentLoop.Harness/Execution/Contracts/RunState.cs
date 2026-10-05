
namespace AgentLoop.Harness.Core;


/// <summary>
/// Estado de la maquina del Run en un instante dado. No es el enum crudo:
/// incluye el pending note del reactor, el elapsed y un reason legible.
/// Asi un Journey muestra "Esperando (plazo agotado con procesos vivos)"
/// en vez de solo "Esperando".
/// </summary>
public sealed record RunState(
    RunStatus Status,
    string? PendingNote,
    TimeSpan Elapsed,
    string? Reason)
{
    public override string ToString()
    {
        var note = string.IsNullOrEmpty(PendingNote) ? string.Empty : $" ({PendingNote})";
        var reason = string.IsNullOrEmpty(Reason) ? string.Empty : $" [{Reason}]";
        return $"{Status}{note}{reason} @ {Elapsed.TotalSeconds:0}s";
    }
}
