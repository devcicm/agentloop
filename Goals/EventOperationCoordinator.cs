using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Harness;
using AgentLoop.Harness.Durable;
using AgentLoop.Parsing;
using AgentLoop.Prompting;
using AgentLoop.Provider;
using AgentLoop.Tools;

namespace AgentLoop.Goals;

public enum OperationStatus { Waiting, Reasoning, Acting, Paused, Succeeded, Failed, Cancelled }
public sealed record EventOperationResult(OperationStatus Status, string Reason, Exception? Error = null)
{
    public bool IsSuccess => Status == OperationStatus.Succeeded;
}

/// <summary>
/// Un actor de operacion. Cada publicacion agenda su handler; no itera estados
/// ni llama StepAsync. Modelo y herramientas publican resultados desde tareas independientes.
/// </summary>
public sealed class EventOperationCoordinator(ILLMClient client, string model, ToolRegistry tools, Scope scope)
{
    private readonly object _mailboxGate = new();
    private readonly object _observerGate = new();
    private Task _observerMailbox = Task.CompletedTask;
    private Task _mailbox = Task.CompletedTask;
    private readonly TaskCompletionSource<EventOperationResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<Guid, ModelDelivery> _models = new();
    private readonly ConcurrentDictionary<Guid, ActionDelivery> _actions = new();
    private readonly Queue<ParsedToolCall> _calls = new();
    private readonly List<AgentEvent> _facts = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationTokenSource _stopGrace = new();
    private GoalContext _context = null!;
    private OperationEventJournal _journal = null!;
    private OperationAdmission _admission = null!;
    private OperationAcceptance _acceptance = null!;
    private bool _started, _terminal, _paused, _modelActive, _reasonQueued, _actionActive;
    private bool _stopGraceScheduled;
    private readonly ConcurrentDictionary<Guid, TaskCompletionSource<bool>> _approvals = new();
    private EventOperationResult? _finish;
    private string? _stopReason;
    private OperationStatus _stopStatus;
    private int _textOnly, _failures, _adaptiveTokens;
    private bool _recovering;
    private ParsedToolCall? _scheduledCall;
    private AgentEvent? _currentEvent;

    public OperationStatus Status { get; private set; } = OperationStatus.Waiting;
    public int MaxModelCalls { get; init; } = 80;
    public int MaxTokens { get; init; } = 2048;
    public double Temperature { get; init; }
    public int MaxNoProgress { get; init; } = 3;
    public int MaxTransientFailures { get; init; } = 3;
    public bool Stream { get; init; }
    public ThinkingPolicy? Thinking { get; init; }
    public IReadOnlyList<ArtifactCriterion>? Criteria { get; init; }
    public string JournalDirectory { get; init; } = System.IO.Path.Combine(Configuration.SecretStore.ResolveRepositoryRoot(), ".agentloop", "event-journal");
    public Action<string>? Trace { get; init; }
    public Action<StreamDelta>? OnDelta { get; init; }
    public Action<ParsedCompletion, TimeSpan>? OnModelCall { get; init; }
    public Action<string, string, bool, TimeSpan>? OnToolCall { get; init; }
    // Captura local de evidencia; estos sinks deben ser breves y no llamar observers externos.
    internal Action<ParsedCompletion, TimeSpan>? RecordModel { get; init; }
    internal Action<string, string, bool, TimeSpan>? RecordTool { get; init; }
    public string JournalPath => _journal.Path;

    public async Task<EventOperationResult> RunAsync(GoalContext context, CancellationToken token = default)
    {
        if (_started) throw new InvalidOperationException("Un coordinador solo admite una operacion.");
        if (MaxModelCalls < 1 || MaxNoProgress < 1 || MaxTransientFailures < 1 || MaxTokens < 64)
            throw new ArgumentOutOfRangeException(nameof(MaxModelCalls), "Presupuestos invalidos.");
        if (!scope.AllowsModel(model)) throw new InvalidOperationException("Modelo fuera del scope.");
        _started = true;
        _context = context;
        _admission = new(scope, context.Ledger);
        _acceptance = new(context.Ledger, Criteria);
        using var journal = _journal = new(JournalDirectory, context.Bus.RunId == Guid.Empty ? Guid.NewGuid() : context.Bus.RunId);
        context.Ledger.ValidationGuard = CheckCard;
        var previousConfirmation = scope.Policy.AsyncConfirm;
        scope.Policy.AsyncConfirm = RequestApprovalAsync;
        using var subscription = context.Bus.Subscribe(Post);
        using var cancellation = token.Register(() => Publish(AgentEventKind.Cancelacion, "cancelado por el operador"));
        try
        {
            context.Messages.Add(ChatMessage.FromSystem(OperationContract.BuildSystemPrompt(context)
                + "\nCOORDINACION POR EVENTOS: usa abrir antes de efectos. Cada resultado exitoso incluye receipt_id."
                + "\nPara comprobar usa evidencia_id=receipt_id de esta transaccion. Cerrar exige comprobacion vigente y procesos finalizados."
                + "\nLas herramientas ejecutadas despues de cerrar se rechazan. No inventes receipt_id. No hace falta producir texto para terminar."
                + (Criteria is null ? "" : "\nContrato de artefactos: " + JsonSerializer.Serialize(Criteria))));
            Publish(AgentEventKind.Objetivo, context.Goal.Text);
            _ = DeadlineAsync(context.Remaining);
            return await _completion.Task.ConfigureAwait(false);
        }
        finally
        {
            _lifetime.Cancel();
            _stopGrace.Cancel();
            context.Ledger.ValidationGuard = null;
            scope.Policy.AsyncConfirm = previousConfirmation;
        }
    }

    public void Control(AgentEventKind kind)
    {
        if (kind is not (AgentEventKind.Cancelacion or AgentEventKind.Pausa or AgentEventKind.Reanudacion or AgentEventKind.ConsultaEstado))
            throw new ArgumentOutOfRangeException(nameof(kind));
        Publish(kind, "control del operador");
    }

    public void Approve(bool approved) => Publish(AgentEventKind.Aprobacion, "decision del operador", new { approved });

    private async ValueTask<bool> RequestApprovalAsync(Terminals.ShellCommand command, CancellationToken token)
    {
        var id = Guid.NewGuid();
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _approvals[id] = completion;
        try
        {
            Publish(AgentEventKind.AprobacionSolicitada, "aprobar/rechazar: " + command.Command,
                new { approval_id = id, comando = command.Command, shell = command.Shell.ToString() });
            return await completion.Task.WaitAsync(token).ConfigureAwait(false);
        }
        finally { _approvals.TryRemove(id, out _); }
    }

    private string? CheckCard(CardKind kind, IReadOnlyDictionary<string, string> fields)
    {
        if (kind == CardKind.Cierre && _context.Supervisor.Running > 0)
            return "hay procesos pendientes; espera sus resultados antes de cerrar";
        return _acceptance.ValidateCard(kind, fields);
    }

    private AgentEvent Publish(AgentEventKind kind, string text, object? data = null)
        => _context.Bus.Publish(kind, "coordinator", text, data, _currentEvent?.EventId);

    private void Post(AgentEvent item)
    {
        lock (_mailboxGate)
            _mailbox = _mailbox.ContinueWith(_ => HandleAsync(item), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
    }

    private Task HandleAsync(AgentEvent item)
    {
        if (_terminal) return Task.CompletedTask;
        try
        {
            _currentEvent = item;
            Observe(() => Trace?.Invoke($"evento #{item.Sequence} {item.Kind}: {item.Text}"));
            _journal.Append("received", item);
            // La cola del bus sirve a consumidores de espera; el actor recibe cada
            // evento por suscripcion y registra su propia confirmacion durable.
            _context.Bus.Drain(1024);
            switch (item.Kind)
            {
                case AgentEventKind.Cancelacion: Stop(OperationStatus.Cancelled, "cancelado por el operador"); break;
                case AgentEventKind.DetencionAgotada:
                    if (_stopReason is not null) Finish(_stopStatus, _stopReason + "; quedaron acciones inciertas, consulta el journal antes de repetir");
                    break;
                case AgentEventKind.Aprobacion:
                    using (var decision = JsonDocument.Parse(item.Data!))
                        foreach (var approval in _approvals.Values.Take(1)) approval.TrySetResult(decision.RootElement.GetProperty("approved").GetBoolean());
                    break;
                case AgentEventKind.DeadlineAlcanzado:
                    _context.DeadlineReached = true;
                    Stop(OperationStatus.Failed, "deadline del objetivo agotado"); break;
                case AgentEventKind.Pausa: _paused = true; Status = OperationStatus.Paused; _context.Supervisor.Pausar(); break;
                case AgentEventKind.Reanudacion:
                    _paused = false;
                    Status = _modelActive ? OperationStatus.Reasoning : _actionActive ? OperationStatus.Acting : OperationStatus.Waiting;
                    _context.Supervisor.Reanudar(); Advance(); break;
                case AgentEventKind.ConsultaEstado:
                    Publish(AgentEventKind.Estado, $"{Status}: {_context.Summary()}"); break;
                case AgentEventKind.RazonamientoRequerido:
                    _reasonQueued = false;
                    if (!_paused && _stopReason is null && !_modelActive && !_actionActive && _calls.Count == 0) StartModel(item);
                    break;
                case AgentEventKind.ModeloResultado: ModelFinished(item); break;
                case AgentEventKind.HerramientaSolicitada:
                    if (!_paused && _stopReason is null && !_actionActive) StartAction(item);
                    break;
                case AgentEventKind.HerramientaResultado: ActionFinished(item); break;
                case AgentEventKind.Objetivo:
                case AgentEventKind.ProcesoIniciado:
                case AgentEventKind.ProcesoSalida:
                case AgentEventKind.ProcesoFin:
                case AgentEventKind.Observador:
                    _facts.Add(item);
                    _context.EventsSeen++;
                    if (_stopReason is not null) TryFinishStop(); else RequestReason();
                    break;
            }
            _journal.Append("applied", item, new { status = Status.ToString(), _context.Cycles, _context.EventsSeen });
        }
        catch (Exception error)
        {
            _context.FailureReason = error.Message;
            _lifetime.Cancel();
            _terminal = true;
            Status = OperationStatus.Failed;
            foreach (var handle in _context.Supervisor.Handles.Where(h => h.Running)) handle.Kill();
            _finish = new(OperationStatus.Failed, "fallo estructural o de registro: " + error.Message, error);
        }
        finally
        {
            _currentEvent = null;
            if (_finish is { } result) { _finish = null; _completion.TrySetResult(result); }
        }
        return Task.CompletedTask;
    }

    private void RequestReason()
    {
        if (_terminal || _paused || _stopReason is not null || _reasonQueued || _modelActive || _actionActive || _scheduledCall is not null || _calls.Count > 0 || _facts.Count == 0) return;
        _reasonQueued = true;
        Publish(AgentEventKind.RazonamientoRequerido, "hay resultados o hechos que requieren una decision");
    }

    private void StartModel(AgentEvent cause)
    {
        if (_context.Cycles >= MaxModelCalls) { Stop(OperationStatus.Failed, "presupuesto de llamadas al modelo agotado"); return; }
        var facts = _facts.ToArray();
        if (facts.Length == 0) return;
        _facts.Clear();
        _context.Messages.Add(ChatMessage.FromUser(_context.Brief(facts)));
        _context.Trim();
        if (JsonSerializer.Serialize(_context.Messages).Length > _context.MaxHistoryCharacters)
        { Stop(OperationStatus.Failed, "contrato o parte actual excede el presupuesto de contexto; consulta evidencia fuera del prompt"); return; }
        var filtered = tools.Filter(scope.AllowsTool).ToDefinitions();
        var request = new ChatRequest
        {
            Model = model, Messages = _context.Messages.ToArray(), Tools = filtered,
            ToolChoice = filtered is null ? null : _recovering ? "required" : "auto", ParallelToolCalls = false, Temperature = Temperature,
            MaxTokens = _recovering ? ResponseBudgets.DuringRecovery(MaxTokens, _adaptiveTokens)
                : Thinking?.EnsureHeadroom(MaxTokens, model) ?? MaxTokens,
            Thinking = _recovering ? ThinkingOptions.Off : Thinking?.For(model),
            ReasoningEffort = NvidiaModelCatalog.IsGlm53(model) ? "low" : null, StreamOptions = Stream ? StreamOptions.WithUsage : null,
        };
        if (JsonSerializer.Serialize(request).Length > 600000) { Stop(OperationStatus.Failed, "contexto excesivo; reduce resultados o antecedentes"); return; }
        _context.Cycles++;
        _modelActive = true;
        Status = OperationStatus.Reasoning;
        var id = Guid.NewGuid();
        var requested = Publish(AgentEventKind.ModeloSolicitado, "solicitud al modelo", new { action_id = id.ToString("N"), model, max_tokens = request.MaxTokens,
            reasoning_effort = request.ReasoningEffort, recovery = _recovering, tool_choice = request.ToolChoice,
            history_characters = JsonSerializer.Serialize(request.Messages).Length, messages = request.Messages.Count,
            numeric_thinking_budget_is_provider_limit = false });
        _journal.Append("admitted", requested);
        _ = Task.Run(() => CollectModelAsync(id, request, requested.EventId));
    }

    private async Task CollectModelAsync(Guid id, ChatRequest request, Guid cause)
    {
        var clock = Stopwatch.StartNew();
        ParsedCompletion? parsed = null;
        Exception? error = null;
        try
        {
            JsonElement raw;
            if (Stream)
            {
                var accumulator = new StreamAccumulator();
                await foreach (var chunk in client.StreamChatCompletionAsync(request, _lifetime.Token).ConfigureAwait(false))
                {
                    var delta = accumulator.Add(chunk);
                    if (!delta.IsEmpty) Observe(() => OnDelta?.Invoke(delta));
                }
                raw = accumulator.BuildRaw();
            }
            else raw = await client.CreateChatCompletionAsync(request, _lifetime.Token).ConfigureAwait(false);
            parsed = CompletionParser.Parse(raw);
        }
        catch (Exception ex) { error = ex; }
        _models[id] = new(parsed, error, clock.Elapsed);
        _context.Bus.Publish(AgentEventKind.ModeloResultado, "model", "respuesta del proveedor", new { action_id = id.ToString("N") }, cause);
    }

    private static Guid DeliveryId(AgentEvent item)
    {
        using var data = JsonDocument.Parse(item.Data!);
        return Guid.Parse(data.RootElement.GetProperty("action_id").GetString()!);
    }

    private void ModelFinished(AgentEvent item)
    {
        if (!_models.TryRemove(DeliveryId(item), out var delivery)) return;
        _modelActive = false;
        _journal.Append("resolved", item, new { response = delivery.Completion?.Raw, error = delivery.Error?.Message });
        if (_stopReason is not null) { TryFinishStop(); return; }
        if (delivery.Error is { } error)
        {
            var transient = error is HttpRequestException or TimeoutException || error is LLMException provider
                && ((int)provider.StatusCode == 0 || TransientErrors.IsRetryableStatus((int)provider.StatusCode));
            if (transient && ++_failures < MaxTransientFailures)
            {
                _ = RecoveryAsync(TimeSpan.FromSeconds(Math.Min(15, 3 * Math.Pow(2, _failures - 1))), "reintento por fallo transitorio");
                return;
            }
            Stop(OperationStatus.Failed, error.Message);
            return;
        }
        _failures = 0;
        var parsed = delivery.Completion!;
        _context.Last = parsed;
        _context.TotalUsage = _context.TotalUsage.Add(parsed.Usage);
        _context.Thinking.Add(parsed.Thinking);
        if (parsed.Thinking.Observed) Thinking?.MarkSupported(model);
        Observe(() => OnModelCall?.Invoke(parsed, delivery.Elapsed));
        RecordModel?.Invoke(parsed, delivery.Elapsed);
        _context.Messages.Add(ChatMessage.FromAssistant(TextSanitizer.Plain(parsed.Content), parsed.RawToolCalls));
        if (parsed.HasToolCalls)
        {
            if (parsed.ToolCalls.Any(c => string.IsNullOrWhiteSpace(c.Id)) || parsed.ToolCalls.Select(c => c.Id).Distinct().Count() != parsed.ToolCalls.Count)
            { Stop(OperationStatus.Failed, "identificadores de herramientas invalidos"); return; }
            _textOnly = 0;
            _adaptiveTokens = 0;
            _recovering = false;
            foreach (var call in parsed.ToolCalls) _calls.Enqueue(call);
            Advance();
        }
        else if (_context.Supervisor.Running == 0)
        {
            if (++_textOnly >= MaxNoProgress) { Stop(OperationStatus.Failed, "sin progreso: el modelo no propone acciones ni tarjetas"); return; }
            _recovering = true;
            if (parsed.FinishReason == "length") _adaptiveTokens = ResponseBudgets.Recovery(MaxTokens);
            _ = RecoveryAsync(TimeSpan.FromMilliseconds(100),
                "recuperacion acotada: la respuesta anterior no ejecuto acciones. Emite una herramienta concreta y pequena; "
                + "crea un archivo por llamada, no escribas el proyecto entero en razonamiento. Usa tarjetas; comprobar exige evidencia_id del resultado.");
        }
        else RequestReason(); // Sin hechos nuevos permanece esperando eventos reales del proceso.
    }

    private void Advance()
    {
        if (_stopReason is not null) { TryFinishStop(); return; }
        if (_paused || _actionActive || _modelActive) return;
        if (_scheduledCall is not null)
        {
            Publish(AgentEventKind.HerramientaSolicitada, _scheduledCall.Name);
            return;
        }
        if (_calls.TryDequeue(out var call))
        {
            _scheduledCall = call;
            Publish(AgentEventKind.HerramientaSolicitada, call.Name);
        }
        else if (_context.Ledger.GoalMet) Finish(OperationStatus.Succeeded, _context.Ledger.Outcome ?? "objetivo cumplido");
        else if (_context.Ledger.Aborted) Stop(OperationStatus.Failed, _context.Ledger.Outcome ?? "operacion abortada");
        else { Status = OperationStatus.Waiting; RequestReason(); }
    }

    private void StartAction(AgentEvent cause)
    {
        if (_scheduledCall is not { } call) return;
        _scheduledCall = null;
        var admission = _admission.Admit(call);
        var transactionId = _context.Ledger.Current?.Id;
        var actionId = Guid.NewGuid();
        _actionActive = true;
        Status = OperationStatus.Acting;
        var admitted = Publish(AgentEventKind.HerramientaSolicitada, "admision de " + call.Name,
            new { action_id = actionId.ToString("N"), call_id = call.Id, tool = call.Name, rejection = admission.Rejection });
        if (admission.Rejection is null) _journal.Append("admitted", admitted);
        _ = Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            ToolExecution execution;
            try
            {
                if (admission.Rejection is not null) execution = new(call.Id, call.Name, JsonSerializer.Serialize(new { error = admission.Rejection }), true);
                else if (admission.Simulated) execution = new(call.Id, call.Name, "{\"simulado\":true,\"executed\":false}", false);
                else
                {
                    if (scope.Policy.Mode == Terminals.PolicyMode.Confirmar && call.Name == "write_text_file"
                        && !await RequestApprovalAsync(new Terminals.ShellCommand(scope.Policy.DefaultShell,
                            "write_text_file " + admission.Call.Arguments.GetProperty("path").GetString()), _lifetime.Token))
                        throw new InvalidOperationException("escritura rechazada por operador");
                    await _context.Supervisor.Puerta.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                    execution = await tools.ExecuteAsync(admission.Call, _lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (Exception error) { execution = new(call.Id, call.Name, JsonSerializer.Serialize(new { error = error.Message }), true); }
            _actions[actionId] = new(execution, transactionId, clock.Elapsed);
            _context.Bus.Publish(AgentEventKind.HerramientaResultado, "tool", call.Name,
                new { action_id = actionId.ToString("N"), call_id = call.Id, tool = call.Name }, admitted.EventId);
        });
    }

    private void ActionFinished(AgentEvent item)
    {
        if (!_actions.TryRemove(DeliveryId(item), out var delivery)) return;
        _actionActive = false;
        var execution = delivery.Execution;
        var output = TextSanitizer.EnsureJsonObject(execution.Output);
        var receipt = _acceptance.Observe(execution with { Output = output }, delivery.TransactionId);
        if (receipt is not null)
        {
            var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(output)!;
            fields["receipt_id"] = JsonSerializer.SerializeToElement(receipt.Id);
            output = JsonSerializer.Serialize(fields);
        }
        _context.Messages.Add(ChatMessage.FromToolResult(execution.ToolCallId, execution.ToolName, output));
        _context.ToolCalls++;
        _context.RememberObservation(execution.ToolName, output, execution.Failed);
        _journal.Append("resolved", item, new { execution.ToolCallId, execution.ToolName, output, execution.Failed, receipt });
        Observe(() => OnToolCall?.Invoke(execution.ToolName, output, !execution.Failed, delivery.Elapsed));
        RecordTool?.Invoke(execution.ToolName, output, !execution.Failed, delivery.Elapsed);
        var fact = Publish(execution.Failed ? AgentEventKind.Error : AgentEventKind.HerramientaFinalizada,
            "resultado registrado de " + execution.ToolName, new { call_id = execution.ToolCallId, receipt_id = receipt?.Id });
        _facts.Add(fact);
        _context.EventsSeen++;
        Advance(); // Las llamadas restantes pasan individualmente por admision, incluso tras cerrar.
    }

    private async Task RecoveryAsync(TimeSpan delay, string reason)
    {
        var cause = _currentEvent?.EventId;
        try
        {
            await Task.Delay(delay, _lifetime.Token).ConfigureAwait(false);
            // Recuperacion es un hecho explicito y acotado; no existe latido periodico.
            _context.Bus.Publish(AgentEventKind.Observador, "recovery", reason, causationId: cause);
        }
        catch (OperationCanceledException) { }
    }

    private async Task DeadlineAsync(TimeSpan remaining)
    {
        try
        {
            await Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, _lifetime.Token).ConfigureAwait(false);
            Publish(AgentEventKind.DeadlineAlcanzado, "deadline del objetivo");
        }
        catch (OperationCanceledException) { }
    }

    private void Stop(OperationStatus status, string reason)
    {
        _stopReason ??= reason;
        _stopStatus = status;
        _context.FailureReason = _stopReason;
        _lifetime.Cancel();
        if (!_stopGraceScheduled)
        {
            _stopGraceScheduled = true;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), _stopGrace.Token);
                    Publish(AgentEventKind.DetencionAgotada, "plazo de drenado agotado");
                }
                catch (OperationCanceledException) { }
            });
        }
        foreach (var handle in _context.Supervisor.Handles.Where(h => h.Running)) handle.Kill();
        TryFinishStop();
    }

    private void TryFinishStop()
    {
        if (_modelActive || _actionActive || _context.Supervisor.Running > 0) return;
        if (_scheduledCall is { } scheduled) { RejectPending(scheduled); _scheduledCall = null; }
        foreach (var call in _calls) RejectPending(call);
        _calls.Clear();
        Finish(_stopStatus, _stopReason!);
    }

    private void RejectPending(ParsedToolCall call)
        => _context.Messages.Add(ChatMessage.FromToolResult(call.Id, call.Name, JsonSerializer.Serialize(new { error = _stopReason, executed = false })));

    private void Finish(OperationStatus status, string reason)
    {
        if (_terminal) return;
        Status = status;
        var item = Publish(status == OperationStatus.Succeeded ? AgentEventKind.ObjetivoCumplido : AgentEventKind.ObjetivoFallido,
            reason, new { status = status.ToString() });
        _journal.Append("terminal", item, new { status = status.ToString(), reason, cards = _context.Ledger.Plays.Select(p => new { p.Name, p.Accepted, p.Fields }), evidence = _context.Ledger.Evidence.Snapshot() });
        _terminal = true;
        _finish = new(status, reason);
    }

    private void Observe(Action action)
    {
        // La instrumentacion no posee el estado operativo y no bloquea al actor.
        lock (_observerGate)
            _observerMailbox = _observerMailbox.ContinueWith(_ => { try { action(); } catch { } },
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private sealed record ModelDelivery(ParsedCompletion? Completion, Exception? Error, TimeSpan Elapsed);
    private sealed record ActionDelivery(ToolExecution Execution, string? TransactionId, TimeSpan Elapsed);
}
