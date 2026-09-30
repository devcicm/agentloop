using System.Text.Json;
using AgentLoop.Parsing;
using AgentLoop.Provider;

namespace AgentLoop.Tools;

/// <summary>Resultado de ejecutar una tool call, listo para volver al modelo.</summary>
public sealed record ToolExecution(string ToolCallId, string ToolName, string Output, bool Failed);

/// <summary>Catalogo de herramientas: genera los schemas y despacha las llamadas.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    public static ToolRegistry Empty { get; } = new([]);

    public int Count => _tools.Count;

    public ToolRegistry Combine(ToolRegistry other) => new(_tools.Values.Concat(other._tools.Values));

    public IReadOnlyCollection<string> Names => _tools.Keys;

    /// <summary>Schemas para el campo "tools" del request, o null si no hay ninguna.</summary>
    public IReadOnlyList<ToolDefinition>? ToDefinitions()
    {
        if (_tools.Count == 0)
        {
            return null;
        }

        return _tools.Values.Select(tool => new ToolDefinition
        {
            Function = new ToolFunction
            {
                Name = tool.Name,
                Description = tool.Description,
                Parameters = tool.Parameters,
            },
        }).ToList();
    }

    /// <summary>
    /// Ejecuta una tool call. Nunca lanza: un nombre desconocido, argumentos rotos o una
    /// excepcion de la herramienta vuelven al modelo como JSON de error para que se corrija.
    /// </summary>
    public async ValueTask<ToolExecution> ExecuteAsync(ParsedToolCall call, CancellationToken cancellationToken)
    {
        if (!_tools.TryGetValue(call.Name, out var tool))
        {
            return new ToolExecution(
                call.Id,
                call.Name,
                Error($"herramienta desconocida: '{call.Name}'. Disponibles: {string.Join(", ", _tools.Keys)}"),
                Failed: true);
        }

        if (call.ArgumentsParseFailed)
        {
            return new ToolExecution(
                call.Id,
                call.Name,
                Error($"los argumentos no son JSON valido: {call.RawArguments}"),
                Failed: true);
        }

        try
        {
            var output = await tool.InvokeAsync(call.Arguments, cancellationToken).ConfigureAwait(false);
            return new ToolExecution(call.Id, call.Name, output, Failed: false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolExecution(call.Id, call.Name, Error($"{ex.GetType().Name}: {ex.Message}"), Failed: true);
        }
    }

    private static string Error(string message)
        => JsonSerializer.Serialize(new { error = message });
}
