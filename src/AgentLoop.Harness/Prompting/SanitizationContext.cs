
namespace AgentLoop.Prompting;


/// <summary>Donde va a acabar el valor que se esta sanitizando.</summary>
public enum SanitizationContext
{
    /// <summary>Texto plano del prompt (no dentro de etiquetas ni de JSON de ejemplo).</summary>
    PromptPlain,

    /// <summary>Dentro de un bloque etiquetado tipo &lt;contexto&gt;...&lt;/contexto&gt; del prompt.</summary>
    PromptXmlTag,

    /// <summary>Dentro de un ejemplo JSON del prompt.</summary>
    PromptJsonExample,

    /// <summary>Contenido de un mensaje role=tool que se envia al modelo.</summary>
    ToolResultMessage,

    /// <summary>Salida del modelo que vuelve a entrar en la conversacion como mensaje assistant.</summary>
    ModelOutput,

    /// <summary>Campo del ledger escrito por el modelo (objetivo, plan, paso, comprobacion, etc.).</summary>
    LedgerField,
}
