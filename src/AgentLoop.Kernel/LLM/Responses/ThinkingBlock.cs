
namespace AgentLoop.Parsing;


/// <summary>Un bloque suelto de razonamiento.</summary>
public sealed record ThinkingBlock(ThinkingBlockKind Kind, string Text, string? Signature = null)
{
    /// <summary>La firma criptografica que algunos proveedores exigen devolver intacta.</summary>
    public bool HasSignature => !string.IsNullOrEmpty(Signature);

    public override string ToString() => Kind switch
    {
        ThinkingBlockKind.Redacted => "[razonamiento censurado]",
        ThinkingBlockKind.Summary => $"(resumen) {Text}",
        _ => Text,
    };
}
