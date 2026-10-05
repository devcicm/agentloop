namespace AgentLoop.Harness.Lifecycle;


/// <summary>
/// Suceso publicado cuando un Run cambia de estado. Lleva el from/to, un
/// timestamp, un motivo legible y un payload opcional para datos
/// estructurados (ciclo en que se tomo la decision, contador al cambio,
/// etc.).
/// </summary>
public sealed record LifecycleEvent(
    Guid RunId,
    RunLifecycleState From,
    RunLifecycleState To,
    DateTimeOffset At,
    string? Reason,
    IReadOnlyDictionary<string, object?>? Payload = null)
{
    public override string ToString()
        => $"#{RunId:N} {At:HH:mm:ss.fff} {From} -> {To}{(string.IsNullOrEmpty(Reason) ? string.Empty : $" ({Reason})")}";
}
