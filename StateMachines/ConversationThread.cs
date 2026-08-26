using AgentLoop.Parsing;
using AgentLoop.Prompting;
using AgentLoop.Provider;

namespace AgentLoop.StateMachines;

/// <summary>
/// El hilo que sobrevive entre comandos del CLI interactivo.
///
/// Guarda la conversacion <b>sin</b> el mensaje de sistema: ese se vuelve a renderizar
/// en cada turno para que {{hora}} y demas parametros esten frescos, en vez de quedar
/// congelados en los que hubiera al arrancar la sesion.
/// </summary>
public sealed class ConversationThread
{
    private readonly List<ChatMessage> _messages = [];

    /// <summary>
    /// Tope de mensajes conservados. El contexto de un modelo es finito y una sesion
    /// larga acaba costando mas en prompt que en respuesta.
    /// </summary>
    public int MaxMessages { get; init; } = 40;

    public PromptTemplate SystemTemplate { get; private set; } = SystemPrompt.Default;

    public PromptVariables Variables { get; } = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Intercambios del usuario (una linea tecleada = uno).</summary>
    public int Exchanges { get; private set; }

    /// <summary>Llamadas al proveedor acumuladas; un intercambio puede gastar varias.</summary>
    public int Turns { get; private set; }

    public TokenUsage TotalUsage { get; private set; } = TokenUsage.Empty;

    public void UseSystemTemplate(PromptTemplate template) => SystemTemplate = template;

    public string RenderSystemPrompt() => SystemTemplate.Render(Variables);

    /// <summary>Prepara el contexto del turno: sistema fresco + historial + nueva entrada.</summary>
    public AgentContext StartTurn(string userPrompt)
        => new(TextSanitizer.Plain(userPrompt), RenderSystemPrompt(), _messages);

    /// <summary>Devuelve al hilo lo que la maquina de estados dejo en el contexto.</summary>
    public void CommitTurn(AgentContext context)
    {
        _messages.Clear();
        _messages.AddRange(context.Messages.Where(m => m.Role != ChatRoles.System));

        Exchanges++;
        Turns += context.Turns;
        TotalUsage = TotalUsage.Add(context.TotalUsage);
        Trim();
    }

    public void Clear()
    {
        _messages.Clear();
        Exchanges = 0;
        Turns = 0;
        TotalUsage = TokenUsage.Empty;
    }

    /// <summary>
    /// Recorta por el principio, pero solo en frontera de turno. Cortar en medio de
    /// una cadena assistant(tool_calls) -> tool dejaria un mensaje role=tool sin su
    /// tool_call, y el gateway responde 400 a eso.
    /// </summary>
    private void Trim()
    {
        if (_messages.Count <= MaxMessages)
        {
            return;
        }

        var drop = _messages.Count - MaxMessages;
        while (drop < _messages.Count && _messages[drop].Role != ChatRoles.User)
        {
            drop++;
        }

        if (drop >= _messages.Count)
        {
            _messages.Clear();
            return;
        }

        _messages.RemoveRange(0, drop);
    }
}
