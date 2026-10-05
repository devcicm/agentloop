using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentLoop.Prompting;
using AgentLoop.Security.Contracts;

namespace AgentLoop.Security;

public static class DataGuard
{
    private static readonly Regex Secrets = new(
        @"\b(?:nvapi-|sk-)[A-Za-z0-9_-]{16,}|\bgh[pousr]_[A-Za-z0-9]{16,}|\bgithub_pat_[A-Za-z0-9_]{16,}|\bBearer\s+[A-Za-z0-9._~+/-]{16,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Injection = new(
        @"ignore\s+(?:all\s+)?(?:previous|prior)\s+instructions|ignora\s+(?:todas\s+)?(?:las\s+)?instrucciones|<\s*/?\s*(?:system|developer)\b|\[INST\]|<\|(?:im_start|system)\|>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static string Redact(string? text) => Secrets.Replace(text ?? "", "[SECRET REDACTED]");
    public static string Display(string? text)
        => Redact(TextSanitizer.Plain(text)).Replace("\u001b", "");

    public static string Input(string text)
    {
        if (text.Length > DataLimits.InputCharacters) throw new InvalidOperationException("Entrada demasiado grande: maximo 32000 caracteres.");
        var clean = TextSanitizer.Plain(text);
        if (string.IsNullOrWhiteSpace(clean)) throw new InvalidOperationException("Entrada vacia tras sanitizar.");
        return Redact(clean);
    }

    public static string PromptData(string source, string text, int maximum = DataLimits.PromptOutputCharacters)
        => JsonSerializer.Serialize(new PromptDataDto(1, source, "untrusted_data",
            TextSanitizer.Truncate(Display(text), maximum)));

    public static string NormalizeToolOutput(string text)
    {
        if (text.Length > DataLimits.ToolOutputCharacters) throw new JsonException("Tool output exceeds its size budget.");
        // Reject ambiguous structured data; plain text remains a supported tool return value.
        var trimmed = text.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('[')) StrictJson.Parse(text, DataLimits.ToolOutputCharacters);
        var normalized = TextSanitizer.EnsureJsonObject(text);
        var root = StrictJson.CleanData(StrictJson.Parse(normalized, DataLimits.ToolOutputCharacters));
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(root.GetRawText())!;
        // Only the host can issue authoritative metadata or a receipt (case variants included).
        foreach (var field in fields.Keys.Where(k => k.Equals("_harness", StringComparison.OrdinalIgnoreCase)
            || k.Equals("receipt_id", StringComparison.OrdinalIgnoreCase)).ToArray()) fields.Remove(field);
        return Redact(JsonSerializer.Serialize(fields));
    }

    private static bool ContainsInstructionLikeData(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String) return Injection.IsMatch(node.GetString()!);
        if (node.ValueKind == JsonValueKind.Object) return node.EnumerateObject().Any(p => ContainsInstructionLikeData(p.Value));
        return node.ValueKind == JsonValueKind.Array && node.EnumerateArray().Any(ContainsInstructionLikeData);
    }

    public static string ForModel(string output, string source)
    {
        output = StrictJson.CleanData(StrictJson.Parse(output)).GetRawText();
        var findings = new List<DataFindingDto>();
        if (ContainsInstructionLikeData(StrictJson.Parse(output))) findings.Add(new("instruction_like_data", source));
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(output)!;
        var truncated = output.Length > DataLimits.PromptOutputCharacters;
        if (truncated)
        {
            var receipt = fields.GetValueOrDefault("receipt_id");
            fields = new() { ["excerpt"] = JsonSerializer.SerializeToElement(TextSanitizer.Truncate(Display(output), DataLimits.PromptOutputCharacters)) };
            if (receipt.ValueKind == JsonValueKind.String) fields["receipt_id"] = receipt;
        }
        return JsonSerializer.Serialize(new ToolResultDto
        {
            Metadata = new(1, source, "untrusted_data", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(output))), truncated, findings),
            Data = fields,
        });
    }
}
