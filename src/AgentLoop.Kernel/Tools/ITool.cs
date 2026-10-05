using System.Text.Json;
namespace AgentLoop.Tools;


/// <summary>Una funcion que el modelo puede invocar.</summary>
public interface ITool
{
    string Name { get; }

    string Description { get; }

    /// <summary>JSON Schema de los parametros que acepta.</summary>
    JsonElement Parameters { get; }

    /// <summary>Ejecuta la herramienta. El string devuelto se le entrega al modelo tal cual.</summary>
    ValueTask<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken);
}
