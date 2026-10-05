using System.Text.Json;
namespace AgentLoop.StateMachines;


/// <summary>
/// Implementacion concreta de la CAPA 3: el ciclo tool_call -> resultado -> respuesta final,
/// portado de tokenrouter-client/tokenrouter/agent.py.
/// </summary>
public class AgentStateMachine : StateMachine<AgentState, ChatTurnContext>
{
    private static readonly Dictionary<AgentState, AgentState[]> Allowed = new()
    {
        [AgentState.Idle] = [AgentState.Requesting, AgentState.Failed],
        [AgentState.Requesting] = [AgentState.Parsing, AgentState.Failed],
        [AgentState.Parsing] = [AgentState.ExecutingTools, AgentState.Responding, AgentState.Failed],
        [AgentState.ExecutingTools] = [AgentState.Requesting, AgentState.Failed],
        [AgentState.Responding] = [AgentState.Completed, AgentState.Failed],
        [AgentState.Completed] = [],
        [AgentState.Failed] = [],
    };

    private readonly ILLMClient _client;
    private readonly ToolRegistry _tools;

    public AgentStateMachine(ILLMClient client, string model, ToolRegistry? tools = null)
        : base(AgentState.Idle)
    {
        _client = client;
        _tools = tools ?? ToolRegistry.Empty;
        Model = model;
    }

    public string Model { get; }

    /// <summary>
    /// 512 por defecto: qwen3.8-max-free consume presupuesto en reasoning_content y con
    /// max_tokens bajo devuelve content=null (ver README de tokenrouter-client).
    /// </summary>
    public int MaxTokens { get; init; } = 512;

    public double Temperature { get; init; } = 0.0;

    public bool ParallelToolCalls { get; init; }

    /// <summary>
    /// Politica de razonamiento explicito. Null = no se manda el parametro nunca, que es
    /// el comportamiento de antes. Con politica puesta, el thinking va activo salvo en los
    /// modelos que ya se sabe que no lo soportan.
    /// </summary>
    public ThinkingPolicy? Thinking { get; init; }

    /// <summary>
    /// Pide la respuesta en streaming. El turno acaba en el mismo sitio: los chunks se
    /// juntan en una respuesta con forma normal, asi que el resto de la maquina no cambia.
    /// </summary>
    public bool Stream { get; init; }

    /// <summary>Cada trozo que llega en streaming, ya normalizado. Solo se invoca si <see cref="Stream"/>.</summary>
    public Action<StreamDelta>? OnDelta { get; init; }

    /// <summary>Callback de traza; recibe una linea legible por paso.</summary>
    public Action<string>? Trace { get; init; }

    protected override AgentState FailureState => AgentState.Failed;

    protected override bool IsTerminal(AgentState state)
        => state is AgentState.Completed or AgentState.Failed;

    protected override bool CanTransition(AgentState from, AgentState to)
        => Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    protected override ValueTask<AgentState> OnExecuteAsync(ChatTurnContext context, CancellationToken cancellationToken)
        => CurrentState switch
        {
            AgentState.Idle => ValueTask.FromResult(AgentState.Requesting),
            AgentState.Requesting => RequestAsync(context, cancellationToken),
            AgentState.Parsing => ValueTask.FromResult(ParseResponse(context)),
            AgentState.ExecutingTools => ExecuteToolsAsync(context, cancellationToken),
            AgentState.Responding => ValueTask.FromResult(Respond(context)),
            _ => ValueTask.FromResult(CurrentState),
        };

    protected override ValueTask<AgentState> OnErrorAsync(Exception error, ChatTurnContext context, CancellationToken cancellationToken)
    {
        context.FailureReason = error is LLMException provider
            ? $"[{provider.StatusCode}] {provider.ProviderMessage}"
            : $"{error.GetType().Name}: {error.Message}";

        Trace?.Invoke($"error: {context.FailureReason}");
        return base.OnErrorAsync(error, context, cancellationToken);
    }

    private async ValueTask<AgentState> RequestAsync(ChatTurnContext context, CancellationToken cancellationToken)
    {
        var definitions = _tools.ToDefinitions();
        var thinking = Thinking?.For(Model);

        var request = new ChatRequest
        {
            Model = Model,
            Messages = context.Messages.ToList(),
            Tools = definitions,
            ToolChoice = definitions is null ? null : "auto",
            ParallelToolCalls = definitions is null ? null : ParallelToolCalls,
            Temperature = Temperature,
            // El presupuesto de razonamiento sale del mismo max_tokens: sin margen extra
            // el modelo se lo gasta pensando y devuelve content vacio.
            MaxTokens = Thinking?.EnsureHeadroom(MaxTokens, Model) ?? MaxTokens,
            Thinking = thinking,
            // Sin include_usage un turno en streaming no se puede contabilizar.
            StreamOptions = Stream ? StreamOptions.WithUsage : null,
        };

        if (JsonSerializer.Serialize(request).Length > 600000)
            throw new InvalidOperationException("El contexto de la peticion supera 600000 caracteres JSON. Reduce la salida de herramientas, usa contexto compactar o inicia un hilo nuevo. Los recuerdos guardados se conservan.");

        context.Turns++;
        var thinkingNote = thinking is null ? string.Empty : $", thinking {thinking}";
        var modeNote = Stream ? ", stream" : string.Empty;
        Trace?.Invoke($"turno {context.Turns}: POST /chat/completions ({context.Messages.Count} mensajes{thinkingNote}{modeNote})");

        context.PendingResponse = Stream
            ? await StreamRequestAsync(request, cancellationToken).ConfigureAwait(false)
            : await _client.CreateChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);

        return AgentState.Parsing;
    }

    /// <summary>
    /// Consume el stream entero y devuelve la respuesta armada. Los deltas se van
    /// entregando segun llegan; el razonamiento tambien, que es justo lo que se quiere ver.
    /// </summary>
    private async Task<JsonElement> StreamRequestAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var accumulator = new StreamAccumulator();

        await foreach (var chunk in _client.StreamChatCompletionAsync(request, cancellationToken).ConfigureAwait(false))
        {
            var delta = accumulator.Add(chunk);
            if (!delta.IsEmpty)
            {
                OnDelta?.Invoke(delta);
            }
        }

        var usageNote = accumulator.HasUsage ? string.Empty : " (sin usage: el backend no lo manda)";
        Trace?.Invoke($"  stream: {accumulator.Chunks} chunks, {accumulator.Content.Length} chars de texto, " +
                      $"{accumulator.ThinkingText.Length} de razonamiento{usageNote}");

        return accumulator.BuildRaw();
    }

    private AgentState ParseResponse(ChatTurnContext context)
    {
        if (context.PendingResponse is null)
        {
            throw new InvalidOperationException("No hay respuesta pendiente que parsear.");
        }

        var parsed = CompletionParser.Parse(context.PendingResponse.Value);
        context.PendingResponse = null;
        context.LastCompletion = parsed;
        context.TotalUsage = context.TotalUsage.Add(parsed.Usage);
        context.Thinking.Add(parsed.Thinking);

        if (parsed.Thinking.Observed)
        {
            // Soporte confirmado por la respuesta, no por una lista: la evidencia manda.
            if (Thinking?.MarkSupported(Model) == true)
            {
                Trace?.Invoke($"  thinking confirmado en {Model} ({parsed.Thinking})");
            }
        }

        // El bloque de tool_calls debe volver al proveedor exactamente como llego, y el
        // razonamiento se reenvia solo si lo hubo: mandar reasoning_content vacio confunde
        // a los backends que lo validan.
        context.Messages.Add(ChatMessage.FromAssistant(
            parsed.Content,
            parsed.RawToolCalls,
            parsed.Thinking.Text is { Length: > 0 } thought ? thought : null));

        if (parsed.HasToolCalls)
        {
            PendingNote = $"{parsed.ToolCalls.Count} tool call(s): {string.Join(", ", parsed.ToolCalls.Select(t => t.Name))}";
            return AgentState.ExecutingTools;
        }

        PendingNote = $"finish_reason={parsed.FinishReason}";
        return AgentState.Responding;
    }

    private async ValueTask<AgentState> ExecuteToolsAsync(ChatTurnContext context, CancellationToken cancellationToken)
    {
        var completion = context.LastCompletion
            ?? throw new InvalidOperationException("No hay tool calls que ejecutar.");

        foreach (var call in completion.ToolCalls)
        {
            var execution = await _tools.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
            context.ToolExecutions.Add(execution);
            // Una tool que devuelve texto suelto o un stack trace descoloca a los modelos
            // que asumen JSON en role=tool; se envuelve en vez de mandarlo crudo.
            var output = DataGuard.ForModel(execution.Output, execution.ToolName);
            context.Messages.Add(ChatMessage.FromToolResult(execution.ToolCallId, execution.ToolName, output));
            Trace?.Invoke($"  tool {call.Name}({call.RawArguments}) -> {Shorten(execution.Output)}");
        }

        return AgentState.Requesting;
    }

    private AgentState Respond(ChatTurnContext context)
    {
        var completion = context.LastCompletion;

        // Un modelo que agoto el presupuesto razonando devuelve content=null: se reporta
        // el razonamiento en vez de dejar el resultado vacio sin explicacion.
        context.FinalContent = completion?.Content
            ?? (completion?.IsThinkingOnly == true
                ? $"(sin content; el modelo se quedo en el razonamiento) {completion.Thinking.Text}"
                : string.Empty);

        return AgentState.Completed;
    }

    private static string Shorten(string value, int max = 120)
        => value.Length <= max ? value : value[..max] + "...";
}
