
namespace AgentLoop.Parsing;


/// <summary>
/// El razonamiento de una respuesta, tipado. Antes era un <c>string?</c> suelto llamado
/// Reasoning, que no distinguia "no penso" de "penso y no me dejan verlo", ni decia
/// cuantos tokens costo.
/// </summary>
public sealed record ThinkingTrace(
    IReadOnlyList<ThinkingBlock> Blocks,
    ThinkingSource Source,
    int TokenCount)
{
    public static readonly ThinkingTrace Empty = new([], ThinkingSource.None, 0);

    /// <summary>No hay ningun bloque: el modelo no razono o no lo expone.</summary>
    public bool IsEmpty => Blocks.Count == 0;

    /// <summary>Hubo razonamiento aunque no se pueda leer (bloques censurados).</summary>
    public bool HasRedacted => Blocks.Any(b => b.Kind == ThinkingBlockKind.Redacted);

    public bool HasSignature => Blocks.Any(b => b.HasSignature);

    /// <summary>El razonamiento legible concatenado. Cadena vacia si todo venia censurado.</summary>
    public string Text => string.Join(
        "\n",
        Blocks.Where(b => b.Kind != ThinkingBlockKind.Redacted && b.Text.Length > 0).Select(b => b.Text));

    public int Length => Text.Length;

    /// <summary>Evidencia de que el modelo soporta thinking: penso algo, aunque este censurado.</summary>
    public bool Observed => Blocks.Count > 0 && (HasRedacted || Text.Trim().Length > 0);

    public static ThinkingTrace FromText(string? text, ThinkingSource source, int tokenCount = 0)
        => string.IsNullOrWhiteSpace(text)
            ? Empty
            : new ThinkingTrace([new ThinkingBlock(ThinkingBlockKind.Thinking, text)], source, tokenCount);

    /// <summary>Copia con el coste en tokens puesto, que se lee de usage y no de los bloques.</summary>
    public ThinkingTrace WithTokenCount(int tokenCount)
        => tokenCount == TokenCount ? this : this with { TokenCount = tokenCount };

    /// <summary>Etiqueta legible del origen; el nombre del enum a secas se lee fatal.</summary>
    public string SourceLabel => Source switch
    {
        ThinkingSource.ReasoningContent => "reasoning_content",
        ThinkingSource.ContentBlocks => "bloques de content",
        ThinkingSource.InlineTags => "<think> en linea",
        ThinkingSource.UsageOnly => "solo coste",
        _ => "ninguno",
    };

    public override string ToString() => IsEmpty
        ? "sin thinking"
        : $"{SourceLabel}: {Blocks.Count} bloque(s), {Length} chars"
          + (TokenCount > 0 ? $", {TokenCount} tokens" : string.Empty)
          + (HasRedacted ? ", censurado" : string.Empty);
}
