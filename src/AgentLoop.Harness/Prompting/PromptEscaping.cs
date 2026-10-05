using System.Text.RegularExpressions;

namespace AgentLoop.Prompting;


/// <summary>Como se escapa un valor al interpolarlo.</summary>
public enum PromptEscaping
{
    /// <summary>Solo limpieza de caracteres de control. Por defecto.</summary>
    Plain,

    /// <summary>Escapa &amp; &lt; &gt; &quot; &apos;: para valores dentro de bloques etiquetados.</summary>
    Xml,

    /// <summary>Escapa como contenido de cadena JSON, sin comillas.</summary>
    Json,

    /// <summary>Sin escapar. Solo para plantillas anidadas de confianza.</summary>
    Raw,
}
