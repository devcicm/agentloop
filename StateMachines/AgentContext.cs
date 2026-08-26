using AgentLoop.Parsing;
using AgentLoop.Provider;
using AgentLoop.Tools;

namespace AgentLoop.StateMachines;

/// <summary>Estado mutable que la maquina de estados va llenando a lo largo del loop.</summary>
public sealed class AgentContext
{
    /// <param name="history">
    /// Conversacion previa, sin mensaje de sistema: lo aporta este constructor recien
    /// renderizado. Asi el hilo se mantiene entre turnos sin arrastrar un system prompt
    /// viejo con la hora o el modelo de hace veinte mensajes.
    /// </param>
    public AgentContext(string userPrompt, string? systemPrompt = null, IEnumerable<ChatMessage>? history = null)
    {
        UserPrompt = userPrompt;

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            Messages.Add(ChatMessage.FromSystem(systemPrompt));
        }

        if (history is not null)
        {
            Messages.AddRange(history.Where(m => m.Role != ChatRoles.System));
        }

        Messages.Add(ChatMessage.FromUser(userPrompt));
    }

    public string UserPrompt { get; }

    /// <summary>Conversacion completa que se reenvia en cada turno.</summary>
    public List<ChatMessage> Messages { get; } = [];

    /// <summary>Respuesta cruda del ultimo request, pendiente de parsear.</summary>
    public System.Text.Json.JsonElement? PendingResponse { get; set; }

    public ParsedCompletion? LastCompletion { get; set; }

    /// <summary>El razonamiento de cada turno, en orden. Vacio si el modelo no lo expone.</summary>
    public List<ThinkingTrace> Thinking { get; } = [];

    /// <summary>Razonamiento del ultimo turno; <see cref="ThinkingTrace.Empty"/> si no hubo.</summary>
    public ThinkingTrace LastThinking => Thinking.Count > 0 ? Thinking[^1] : ThinkingTrace.Empty;

    /// <summary>El modelo razono en algun turno de este contexto.</summary>
    public bool ThinkingObserved => Thinking.Any(t => t.Observed);

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
