using System.Text.Json;

namespace AgentLoop.Provider;


/// <summary>Esquema de una funcion expuesta al modelo.</summary>
public sealed class ToolFunction
{

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    /// <summary>JSON Schema de los parametros.</summary>

    public required JsonElement Parameters { get; init; }
}
