
namespace AgentLoop.Parsing;


/// <summary>Tipo de bloque de razonamiento devuelto por el modelo.</summary>
public enum ThinkingBlockKind
{
    /// <summary>Razonamiento en claro.</summary>
    Thinking,

    /// <summary>El proveedor censuro el contenido pero confirma que hubo razonamiento.</summary>
    Redacted,

    /// <summary>Resumen del razonamiento, no la cadena completa.</summary>
    Summary,
}
