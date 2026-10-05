
namespace AgentLoop.Harness.Observability;


/// <summary>
/// Linea de log estructurada: timestamp, nivel, scope, mensaje y datos opcionales.
/// Pensada para que cualquier observer la serialice como quiera (consola, JSON,
/// OpenTelemetry, lo que sea) sin acoplarse a un formato concreto.
/// </summary>
public sealed record StructuredLog(
    DateTimeOffset At,
    LogLevel Level,
    string Scope,
    string Message,
    IReadOnlyDictionary<string, object?>? Data = null)
{
    public override string ToString()
    {
        var data = Data is null || Data.Count == 0 ? string.Empty : $" data={System.Text.Json.JsonSerializer.Serialize(Data)}";
        return $"{At:HH:mm:ss.fff} {Level,-7} [{Scope}] {Message}{data}";
    }
}
