
namespace AgentLoop.Discovery;


/// <summary>Nombres de las sondas, estables para que el JSON de salida sea comparable entre corridas.</summary>
public static class CapabilityNames
{
    public const string Accessible = "accessible";
    public const string Chat = "chat";
    public const string ToolCalling = "tool_calling";
    public const string JsonMode = "json_mode";
    public const string SystemMessage = "system_message";

    /// <summary>El modelo acepta el parametro thinking y devuelve razonamiento.</summary>
    public const string Thinking = "thinking";

    /// <summary>Las tres que un modelo debe pasar para considerarse agentico.</summary>
    public static readonly string[] RequiredForAgentic = [Accessible, Chat, ToolCalling];

    public static readonly string[] All = [Accessible, Chat, ToolCalling, JsonMode, SystemMessage, Thinking];
}
