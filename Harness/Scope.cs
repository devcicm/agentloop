using AgentLoop.Terminals;

namespace AgentLoop.Harness;

/// <summary>
/// Frontera de un Run: que modelos, paths y tools estan autorizados, encima
/// de las reglas duras de <see cref="ExecutionPolicy"/>. La policy veta
/// comandos por regex; el Scope acota que se le ofrece al modelo en el
/// system prompt y en las tools disponibles.
///
/// AllowedPaths es una whitelist de raices; cualquier comando que intente
/// actuar fuera se rechaza antes de tocar ExecutionPolicy. AllowedModels
/// evita que el agente cambie de modelo a algo no acordado. AllowedTools,
/// si no esta vacio, restringe las tools que el OperationTools expone.
/// </summary>
public sealed record Scope(
    ExecutionPolicy Policy,
    IReadOnlyList<string> AllowedModels,
    IReadOnlyList<string> AllowedPaths,
    IReadOnlyList<string> AllowedTools)
{
    /// <summary>Scope minimo: la policy sola, sin whitelists adicionales.</summary>
    public static Scope Minimal(ExecutionPolicy policy)
        => new(policy, [], [], []);

    /// <summary>Devuelve true si <paramref name="model"/> esta permitido (o si no hay whitelist).</summary>
    public bool AllowsModel(string model)
        => AllowedModels.Count == 0 || AllowedModels.Any(m => string.Equals(m, model, StringComparison.OrdinalIgnoreCase));

    /// <summary>Devuelve true si <paramref name="tool"/> esta permitida (o si no hay whitelist).</summary>
    public bool AllowsTool(string tool)
        => AllowedTools.Count == 0 || AllowedTools.Any(t => string.Equals(t, tool, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Devuelve true si <paramref name="path"/> cae dentro de alguno de los
    /// AllowedPaths (o si no hay whitelist). Compara por prefijo normalizado.
    /// </summary>
    public bool AllowsPath(string path)
    {
        if (AllowedPaths.Count == 0)
        {
            return true;
        }

        var full = Path.GetFullPath(path);
        return AllowedPaths.Any(root =>
        {
            var candidate = Path.GetFullPath(root);
            return full.StartsWith(candidate, StringComparison.OrdinalIgnoreCase);
        });
    }
}
