using AgentLoop.Operations;
using System.Text.Json;
namespace AgentLoop.StateMachines;


/// <summary>
/// El hilo que sobrevive entre comandos del CLI interactivo.
///
/// Guarda la conversacion <b>sin</b> el mensaje de sistema: ese se vuelve a renderizar
/// en cada turno para que {{hora}} y demas parametros esten frescos, en vez de quedar
/// congelados en los que hubiera al arrancar la sesion.
/// </summary>
public sealed class ConversationSession
{
    private readonly List<ChatMessage> _messages = [];
    public OperationIntentState TaskState { get; private set; } = new(OperationIntent.General,"",[]);
    public void SetIntent(OperationIntent intent) => TaskState=TaskState with { Intent=intent };
    public void BindObjective(string objective)
    {
        objective=DataGuard.Input(objective);
        if (objective.Length>6000) throw new ArgumentException("Objetivo fijado fuera de presupuesto.");
        TaskState=TaskState with { Objective=objective };
    }
    public void AddConstraint(string constraint)
    {
        constraint=DataGuard.Input(constraint);
        if (TaskState.Constraints.Count>=32 || TaskState.Constraints.Sum(s=>s.Length)+constraint.Length>8000) throw new ArgumentException("Restricciones fuera de presupuesto.");
        TaskState=TaskState with { Constraints=TaskState.Constraints.Append(constraint).Distinct().ToArray() };
    }
    public void ClearConstraints() => TaskState=TaskState with { Constraints=[] };
    public IConversationSessionStore? SessionStore { get; init; }
    private IConversationSessionStore Store => SessionStore ?? throw new InvalidOperationException("No hay adaptador de almacenamiento de sesiones configurado.");

    /// <summary>
    /// Tope de mensajes conservados. El contexto de un modelo es finito y una sesion
    /// larga acaba costando mas en prompt que en respuesta.
    /// </summary>
    public int MaxMessages { get; init; } = 40;

    public int MaxHistoryCharacters { get; private set; } = 48000;
    public int HistoryCharacters => JsonSerializer.Serialize(_messages).Length;
    public int DroppedMessages { get; private set; }
    public IConversationMemory? Memory { get; set; }

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

    public void SaveSession(string path) => Store.Write(path,
        JsonSerializer.Serialize(new { version=2,messages=_messages,task_state=TaskState }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));

    public void LoadSession(string path)
    {
        var document=StrictJson.Parse(Store.Read(path,4000000),4000000);
        var root=document;
        var state=TaskState;
        if (root.ValueKind==JsonValueKind.Object)
        {
            state=JsonSerializer.Deserialize<OperationIntentState>(root.GetProperty("task_state").GetRawText(),new JsonSerializerOptions { PropertyNamingPolicy=JsonNamingPolicy.SnakeCaseLower }) ?? throw new InvalidOperationException("Estado de tarea invalido.");
            if (!Enum.IsDefined(state.Intent) || state.Objective is null || state.Objective.Length>6000 || state.Constraints is null || state.Constraints.Count>32 || state.Constraints.Any(c=>c is null) || state.Constraints.Sum(c=>c.Length)>8000)
                throw new InvalidOperationException("Estado de tarea fuera de contrato.");
            if (root.GetProperty("version").GetInt32()!=2) throw new InvalidOperationException("Version de sesion no admitida.");
            state=state with { Objective=DataGuard.Input(state.Objective), Constraints=state.Constraints.Select(DataGuard.Input).ToArray() };
            root=root.GetProperty("messages");
        }
        var messages = JsonSerializer.Deserialize<List<ChatMessage>>(root.GetRawText(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true })
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

    public PromptVariables Variables { get; init; } = new();

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>Intercambios del usuario (una linea tecleada = uno).</summary>
    public int Exchanges { get; private set; }

    /// <summary>Llamadas al proveedor acumuladas; un intercambio puede gastar varias.</summary>
    public int Turns { get; private set; }

    public TokenUsage TotalUsage { get; private set; } = TokenUsage.Empty;

    public void UseSystemTemplate(PromptTemplate template) => SystemTemplate = template;

    public PromptRenderOptions RenderOptions { get; init; } = new();
    public PromptRenderMetadata? LastRender { get; private set; }
    public string RenderSystemPrompt()
    {
        var rendered=SystemTemplate.RenderResult(Variables,RenderOptions);
        if (!rendered.IsOk) throw new FailureException(rendered.Error!);
        LastRender=rendered.Value!.Metadata;
        var memory=Memory?.Render() ?? "";
        var result=rendered.Value.Text + (memory.Length==0 ? "" : "\nDATOS DE MEMORIA DEL OPERADOR:\n"+memory);
        if (result.Length > RenderOptions.MaxCharacters || System.Text.Encoding.UTF8.GetByteCount(result) > RenderOptions.MaxBytes)
            throw new FailureException(new("prompt.memory_budget","Prompt y memoria no caben en el presupuesto de render.",Guid.NewGuid().ToString("N")));
        return result;
    }

    /// <summary>Prepara el contexto del turno: sistema fresco + historial + nueva entrada.</summary>
    public ChatTurnContext StartTurn(string userPrompt)
        => new(DataGuard.Input(userPrompt), RenderSystemPrompt(), _messages);

    /// <summary>Devuelve al hilo lo que la maquina de estados dejo en el contexto.</summary>
    public void CommitTurn(ChatTurnContext context)
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
        TaskState=TaskState with { Objective="",Constraints=[] };
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
