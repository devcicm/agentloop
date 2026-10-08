using System.Security.Cryptography;
using System.Text;
using AgentLoop.Harness.Functional;

namespace AgentLoop.Operations;

/// <summary>Distingue observaciones nuevas de repeticion; errores repetidos nunca acreditan progreso.</summary>
public sealed class ProgressTracker(HarnessPolicy policy)
{
    private readonly Dictionary<string, (string Output, int Repeats)> _actions = [];
    private int _exploration;
    public int WithoutProgress { get; private set; }
    public Result<bool, Failure> Observe(string tool, string arguments, string output, bool failed, bool material, string correlation)
    {
        var key = Hash(tool + "\n" + StableArguments(arguments));
        var fingerprint = Hash(StableOutput(output));
        var repeats = _actions.TryGetValue(key, out var old) && old.Output == fingerprint ? old.Repeats + 1 : 1;
        _actions[key] = (fingerprint, repeats);
        var progress = !failed && (material || repeats == 1 && _exploration++ < policy.MaxExplorationActions);
        WithoutProgress = progress ? 0 : WithoutProgress + 1;
        if (repeats > policy.MaxRepeatedActions || WithoutProgress > policy.MaxActionsWithoutProgress)
            return Result<bool, Failure>.Fail(new("operation.stalled", "Acciones repetidas sin nueva evidencia; cambia de estrategia o solicita informacion.", correlation));
        return Result<bool, Failure>.Ok(progress);
    }
    private static string StableArguments(string text)
    {
        try { using var json=System.Text.Json.JsonDocument.Parse(text); return CanonicalJson.Render(json.RootElement); }
        catch (System.Text.Json.JsonException) { return text; }
    }
    private static string StableOutput(string output)
    {
        try
        {
            using var data = System.Text.Json.JsonDocument.Parse(output);
            if (data.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return output;
            var volatileFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "read_receipt", "receipt_id", "at", "timestamp", "next", "_harness" };
            return System.Text.Json.JsonSerializer.Serialize(data.RootElement.EnumerateObject().Where(p => !volatileFields.Contains(p.Name)).OrderBy(p => p.Name).ToDictionary(p => p.Name, p => p.Value));
        }
        catch (System.Text.Json.JsonException) { return output; }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
