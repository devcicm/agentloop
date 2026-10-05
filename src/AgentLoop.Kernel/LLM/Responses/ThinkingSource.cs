
namespace AgentLoop.Parsing;


/// <summary>De donde salio la traza. Sirve para saber si el modelo soporta thinking de verdad.</summary>
public enum ThinkingSource
{
    /// <summary>El modelo no razono, o no lo expone.</summary>
    None,

    /// <summary>Campo <c>message.reasoning_content</c>: lo habitual en este gateway.</summary>
    ReasoningContent,

    /// <summary>Array de bloques en <c>content</c> con <c>type: thinking</c>.</summary>
    ContentBlocks,

    /// <summary>Etiquetas &lt;think&gt;...&lt;/think&gt; incrustadas en el texto de la respuesta.</summary>
    InlineTags,

    /// <summary>Solo consta el coste en tokens: el proveedor cobra el razonamiento pero no lo ensena.</summary>
    UsageOnly,
}
