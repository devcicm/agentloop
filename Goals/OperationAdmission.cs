using System.Text.Json;
using AgentLoop.Cards;
using AgentLoop.Harness;
using AgentLoop.Parsing;
using AgentLoop.Terminals;

namespace AgentLoop.Goals;

/// <summary>Puerta comun de tarjetas y politica, aplicada antes de cada herramienta.</summary>
public sealed class OperationAdmission(Scope scope, CardLedger ledger)
{
    private static readonly HashSet<string> ReadOnly = new(StringComparer.Ordinal)
    { "read_text_file", "list_directory", "find_files", "get_system_context", "where_command", "get_time", "add_numbers", "procesos", "leer" };
    public static bool IsReadOnly(string name) => ReadOnly.Contains(name);

    public (ParsedToolCall Call, string? Rejection, bool Simulated) Admit(ParsedToolCall call)
    {
        if (ledger.Finished) return (call, "la operacion ya esta cerrada; no se admiten acciones", false);
        if (!scope.AllowsTool(call.Name)) return (call, "herramienta fuera del scope", false);
        if (call.ArgumentsParseFailed) return (call, "argumentos no validos", false);
        if (call.Name != "tarjeta" && !IsReadOnly(call.Name) && ledger.OpenCount == 0)
            return (call, "abre una transaccion antes de admitir efectos", false);
        if (scope.AllowedPaths.Count > 0 && call.Name is "ejecutar" or "lanzar")
            return (call, "shell arbitrario no admite restriccion de rutas; usa herramientas de archivo tipadas", false);
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(call.RawArguments)!;
            foreach (var field in new[] { "path", "root" })
            {
                if (!args.TryGetValue(field, out var value) || value.ValueKind != JsonValueKind.String) continue;
                var path = System.IO.Path.GetFullPath(value.GetString()!, scope.Policy.WorkingDirectory);
                if (!scope.AllowsPath(path)) return (call, "ruta fuera del scope", false);
                if (scope.AllowedPaths.Count > 0)
                {
                    for (var node = path; !string.IsNullOrEmpty(node); node = System.IO.Path.GetDirectoryName(node))
                        if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0)
                            return (call, "ruta con enlace: no puede acreditarse el scope", false);
                }
                args[field] = JsonSerializer.SerializeToElement(path);
            }
            var normalized = JsonSerializer.SerializeToElement(args);
            call = call with { Arguments = normalized, RawArguments = normalized.GetRawText() };
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException or IOException)
        { return (call, "argumentos/ruta invalidos: " + ex.Message, false); }
        var simulated = scope.Policy.Mode == PolicyMode.Simular && call.Name != "tarjeta" && !IsReadOnly(call.Name);
        return (call, null, simulated);
    }
}
