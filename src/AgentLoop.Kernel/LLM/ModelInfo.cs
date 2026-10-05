using System.Text.Json;

namespace AgentLoop.Provider;


/// <summary>Entrada de GET /models.</summary>
public sealed record ModelInfo(
    string Id,
    string OwnedBy,
    long Created,
    IReadOnlyList<string> SupportedEndpointTypes,
    string Tags)
{
    private static readonly string[] ChatEndpoints =
        ["openai", "openai-response", "anthropic", "anthropic-compatible", "gemini"];

    public bool IsFree =>
        Id.EndsWith(":free", StringComparison.OrdinalIgnoreCase) ||
        Id.EndsWith("-free", StringComparison.OrdinalIgnoreCase) ||
        Id.Contains(":free", StringComparison.OrdinalIgnoreCase);

    public bool IsText => Tags.Contains("Text", StringComparison.OrdinalIgnoreCase);

    public bool SupportsChat =>
        SupportedEndpointTypes.Any(e => ChatEndpoints.Contains(e, StringComparer.OrdinalIgnoreCase));
}
