using System.Text.Json;

namespace AgentLoop.Provider;


/// <summary>
/// Ajustes del modo streaming. <c>include_usage</c> hace que llegue un ultimo chunk con
/// el consumo de tokens: sin el, una respuesta en streaming no se puede contabilizar.
/// </summary>
public sealed class StreamOptions
{
    public static readonly StreamOptions WithUsage = new() { IncludeUsage = true };

    public bool? IncludeUsage { get; init; }
}
