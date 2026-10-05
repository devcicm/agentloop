using System.Text.Json;

namespace AgentLoop.Provider;


/// <summary>Roles del protocolo OpenAI-compatible.</summary>
public static class ChatRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}
