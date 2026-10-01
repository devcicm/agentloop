using System.Text.Json;
using AgentLoop.Parsing;
using AgentLoop.Provider;
using AgentLoop.Security;
using AgentLoop.Security.Contracts;
using AgentLoop.Harness;

namespace AgentLoop.Tools;

/// <summary>Resultado de ejecutar una tool call, listo para volver al modelo.</summary>
public sealed record ToolExecution(string ToolCallId, string ToolName, string Output, bool Failed)
{
    public bool EffectsMayHaveOccurred { get; init; }
}

/// <summary>Catalogo de herramientas: genera los schemas y despacha las llamadas.</summary>
public sealed class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools;
    private readonly Scope? _scope;

    public ToolRegistry(IEnumerable<ITool> tools, Scope? scope = null)
    {
        _scope = scope;
        _tools = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
    }

    public static ToolRegistry Empty { get; } = new([]);

    public int Count => _tools.Count;

    public ToolRegistry Combine(ToolRegistry other)
    {
        if (_scope is not null && other._scope is not null && _scope != other._scope)
            throw new InvalidOperationException("No se combinan registros con scopes distintos.");
        return new(_tools.Values.Concat(other._tools.Values), _scope ?? other._scope);
    }
    public ToolRegistry Filter(Func<string, bool> predicate) => new(_tools.Values.Where(t => predicate(t.Name)), _scope);
    public ToolRegistry WithScope(Scope scope) => new(_tools.Values.Where(t => scope.AllowsTool(t.Name)), scope);

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

    public string? ValidateArguments(ParsedToolCall call)
    {
        if (_scope is not null && !_scope.AllowsTool(call.Name)) return "herramienta fuera del scope";
        if (!_tools.TryGetValue(call.Name, out var tool)) return "herramienta desconocida";
        var rejection = ToolArgumentValidator.Validate(call, tool.Parameters);
        if (rejection is not null) return rejection;
        foreach (var name in new[] { "path", "root" })
        {
            if (!call.Arguments.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) continue;
            var path = value.GetString()!;
            if (path.Any(char.IsControl) || path != AgentLoop.Prompting.TextSanitizer.Plain(path)) return "ruta contiene caracteres inseguros";
            try
            {
                var absolute = Path.GetFullPath(path, _scope?.Policy.WorkingDirectory ?? Environment.CurrentDirectory);
                if (_scope is not null && !_scope.AllowsPath(absolute)) return "ruta fuera del scope";
                if (ProtectedResources.HasUnsafeWindowsAlias(absolute)) return "alias de ruta inseguro";
                if (ProtectedResources.IsProtected(absolute)) return "recurso protegido del harness";
                for (var node = absolute; !string.IsNullOrEmpty(node); node = Path.GetDirectoryName(node))
                    if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0)
                        return "ruta con enlace no permitida";
            }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException or UnauthorizedAccessException)
            { return "ruta invalida o inaccesible"; }
        }
        return null;
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

        if (ValidateArguments(call) is { } rejection)
        {
            return new ToolExecution(
                call.Id,
                call.Name,
                Error(rejection),
                Failed: true);
        }

        var invoked = false;
        try
        {
            var arguments = call.Arguments;
            if (_scope is not null)
            {
                var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments.GetRawText())!;
                foreach (var name in new[] { "path", "root" })
                    if (fields.TryGetValue(name, out var path) && path.ValueKind == JsonValueKind.String)
                        fields[name] = JsonSerializer.SerializeToElement(Path.GetFullPath(path.GetString()!, _scope.Policy.WorkingDirectory));
                arguments = JsonSerializer.SerializeToElement(fields);
            }
            invoked = true;
            var output = DataGuard.NormalizeToolOutput(await tool.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false));
            var failed = false;
            try
            {
                using var response = JsonDocument.Parse(output);
                if (response.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var root = response.RootElement;
                    failed = root.TryGetProperty("error", out _)
                        || root.TryGetProperty("aceptada", out var accepted) && accepted.ValueKind == JsonValueKind.False
                        || root.TryGetProperty("agotado_el_tiempo", out var timedOut) && timedOut.ValueKind == JsonValueKind.True
                        || root.TryGetProperty("codigo", out var code) && code.ValueKind == JsonValueKind.Number && code.GetInt32() != 0;
                }
            }
            catch (JsonException) { }
            return new ToolExecution(call.Id, call.Name, output, Failed: failed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolExecution(call.Id, call.Name,
                JsonSerializer.Serialize(new ToolFailureDto(DataGuard.Display($"{ex.GetType().Name}: {ex.Message}"), invoked)), Failed: true)
                { EffectsMayHaveOccurred = invoked };
        }
    }

    private static string Error(string message)
        => JsonSerializer.Serialize(new ToolFailureDto(DataGuard.Display(message)));
}
