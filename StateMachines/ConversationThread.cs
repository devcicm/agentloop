using AgentLoop.Parsing;
using AgentLoop.Prompting;
using AgentLoop.Provider;
using System.Text.Json;
using AgentLoop.Security;

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

    public int MaxHistoryCharacters { get; private set; } = 48000;
    public int HistoryCharacters => JsonSerializer.Serialize(_messages).Length;
    public int DroppedMessages { get; private set; }
    public ConversationMemory? Memory { get; set; }

    public void SetHistoryLimit(int characters)
    {
        if (characters < 2000 || characters > 500000)
            throw new InvalidOperationException("El limite del historial debe estar entre 2000 y 500000 caracteres.");
        MaxHistoryCharacters = characters;
        Trim();
    }

    public void Compact()
    {
        var lastUser = _messages.FindLastIndex(m => m.Role == ChatRoles.User);
        if (lastUser > 0) { DroppedMessages += lastUser; _messages.RemoveRange(0, lastUser); }
        Trim();
    }

    public void SaveSession(string path) => ConversationMemory.AtomicWrite(path,
        JsonSerializer.Serialize(_messages, new JsonSerializerOptions { WriteIndented = true }));

    public void LoadSession(string path)
    {
        if (new FileInfo(path).Length > 4000000) throw new InvalidOperationException("La sesion supera 4 MB.");
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(StrictJson.Parse(File.ReadAllText(path), 4000000).GetRawText())
            ?? throw new InvalidOperationException("Sesion vacia o invalida.");
        var pending = new HashSet<string>(StringComparer.Ordinal);
        foreach (var message in messages)
        {
            if (message is null) throw new InvalidOperationException("La sesion contiene un mensaje nulo.");
            if (message.Role is not (ChatRoles.User or ChatRoles.Assistant or ChatRoles.Tool))
                throw new InvalidOperationException("Rol no permitido en el historial.");
            if (message.Role != ChatRoles.Tool && pending.Count > 0)
                throw new InvalidOperationException("Sesion con resultados de herramientas pendientes.");
            if (message.Role == ChatRoles.Tool && !pending.Remove(message.ToolCallId ?? ""))
                throw new InvalidOperationException("Resultado de herramienta huerfano.");
            if (message.ToolCalls is { } calls)
            {
                if (message.Role != ChatRoles.Assistant || calls.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("Llamadas de herramienta invalidas.");
                foreach (var call in calls.EnumerateArray())
                    if (!call.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(id.GetString()) || !pending.Add(id.GetString()!))
                        throw new InvalidOperationException("Identificador de herramienta invalido o duplicado.");
            }
        }
        if (pending.Count > 0 || messages.Count > 0 && (messages[0].Role != ChatRoles.User || messages[^1].Role != ChatRoles.Assistant || messages[^1].ToolCalls is not null))
            throw new InvalidOperationException("La sesion debe contener intercambios completos.");
        Clear();
        _messages.AddRange(messages.Select(m => new ChatMessage { Role = m.Role, Content = DataGuard.Display(m.Content),
            Name = m.Name, ToolCallId = m.ToolCallId, ToolCalls = m.ToolCalls }));
        Exchanges = messages.Count(m => m.Role == ChatRoles.User);
        Trim();
    }

    public PromptTemplate SystemTemplate { get; private set; } = SystemPrompt.Default;

    public PromptVariables Variables { get; } = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Intercambios del usuario (una linea tecleada = uno).</summary>
    public int Exchanges { get; private set; }

    /// <summary>Llamadas al proveedor acumuladas; un intercambio puede gastar varias.</summary>
    public int Turns { get; private set; }

    public TokenUsage TotalUsage { get; private set; } = TokenUsage.Empty;

    public void UseSystemTemplate(PromptTemplate template) => SystemTemplate = template;

    public string RenderSystemPrompt() => SystemTemplate.Render(Variables) + Memory?.Render();

    /// <summary>Prepara el contexto del turno: sistema fresco + historial + nueva entrada.</summary>
    public AgentContext StartTurn(string userPrompt)
        => new(DataGuard.Input(userPrompt), RenderSystemPrompt(), _messages);

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
        DroppedMessages = 0;
    }

    /// <summary>
    /// Recorta por el principio, pero solo en frontera de turno. Cortar en medio de
    /// una cadena assistant(tool_calls) -> tool dejaria un mensaje role=tool sin su
    /// tool_call, y el gateway responde 400 a eso.
    /// </summary>
    private void Trim()
    {
        if (_messages.Count <= MaxMessages && HistoryCharacters <= MaxHistoryCharacters)
        {
            return;
        }

        var drop = Math.Max(1, _messages.Count - MaxMessages);
        while (drop < _messages.Count && JsonSerializer.Serialize(_messages.Skip(drop)).Length > MaxHistoryCharacters)
            drop++;
        while (drop < _messages.Count && _messages[drop].Role != ChatRoles.User)
        {
            drop++;
        }

        if (drop >= _messages.Count)
        {
            DroppedMessages += _messages.Count;
            _messages.Clear();
            return;
        }

        DroppedMessages += drop;
        _messages.RemoveRange(0, drop);
    }
}
