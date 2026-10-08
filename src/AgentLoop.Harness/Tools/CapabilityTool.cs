using System.Text.Json;
namespace AgentLoop.Tools;

/// <summary>Metadatos fijados por composicion confiable; los argumentos del modelo no pueden cambiarlos.</summary>
public sealed class CapabilityTool(ITool inner, ToolCapabilities capabilities) : ITool
{
    public string Name => inner.Name;
    public string Description => inner.Description;
    public JsonElement Parameters => inner.Parameters;
    public ToolCapabilities Capabilities => capabilities;
    public ValueTask<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken) => inner.InvokeAsync(arguments, cancellationToken);
}
