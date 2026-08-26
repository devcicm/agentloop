using AgentLoop.Parsing;
using AgentLoop.Provider;
using AgentLoop.Tools;

namespace AgentLoop.StateMachines;

/// <summary>Estado mutable que la maquina de estados va llenando a lo largo del loop.</summary>
public sealed class AgentContext
{
    public AgentContext(string userPrompt, string? systemPrompt = null)
    {
        UserPrompt = userPrompt;
        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            Messages.Add(ChatMessage.FromSystem(systemPrompt));
        }

        Messages.Add(ChatMessage.FromUser(userPrompt));
    }

    public string UserPrompt { get; }

    /// <summary>Conversacion completa que se reenvia en cada turno.</summary>
    public List<ChatMessage> Messages { get; } = [];

    /// <summary>Respuesta cruda del ultimo request, pendiente de parsear.</summary>
    public System.Text.Json.JsonElement? PendingResponse { get; set; }

    public ParsedCompletion? LastCompletion { get; set; }

    public List<ToolExecution> ToolExecutions { get; } = [];

    public TokenUsage TotalUsage { get; set; } = TokenUsage.Empty;

    /// <summary>Numero de llamadas al proveedor realizadas.</summary>
    public int Turns { get; set; }

    /// <summary>
    /// Respuesta final del agente. Sigue en null hasta que el modelo deja de pedir herramientas;
    /// es justo la condicion que rompe el loop de la CAPA 4.
    /// </summary>
    public string? FinalContent { get; set; }

    public string? FailureReason { get; set; }
}
