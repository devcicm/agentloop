using System.Text.Json;
using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Harness;
using AgentLoop.Harness.Durable;
using AgentLoop.Parsing;
using AgentLoop.Provider;
using AgentLoop.Terminals;
using AgentLoop.Tools;

namespace AgentLoop.Goals;

/// <summary>Pruebas de contratos observables del coordinador de eventos, sin red.</summary>
public static class EventCoordinatorSelfTest
{
    public static async Task<bool> RunAsync(CancellationToken token = default)
    {
        var results = new List<(string Name, bool Ok)>();
        void Check(string name, bool ok) => results.Add((name, ok));
        var root = Path.Combine(Path.GetTempPath(), "agentloop-coordinator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        object Call(string name, object args, string id) => new { id, type = "function", function = new { name, arguments = JsonSerializer.Serialize(args) } };
        object Open(string id = "open") => Call("tarjeta", new { nombre = "abrir", objetivo = "contrato de prueba", plan = "ejecutar y comprobar" }, id);
        object Abort(string id = "abort") => Call("tarjeta", new { nombre = "abortar", motivo = "fin controlado de prueba" }, id);
        string Receipt(ChatRequest request, string tool)
        {
            using var output = JsonDocument.Parse(request.Messages.Last(m => m.Role == ChatRoles.Tool && m.Name == tool).Content!);
            return output.RootElement.GetProperty("receipt_id").GetString()!;
        }
        object CheckCard(string receipt, string id = "check") => Call("tarjeta", new { nombre = "comprobar", comprobacion = "resultado observado", resultado = "40", evidencia = "herramienta", evidencia_id = receipt }, id);
        object Close(string id = "close") => Call("tarjeta", new { nombre = "cerrar", resultado = "40", evidencia = "resultado comprobado por sistema" }, id);

        using (var fixture = new Fixture(root, "antes"))
        {
            var path = Path.Combine(root, "antes.txt");
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Call("write_text_file", new { path, content = "no permitido" }, "write")) : Response()));
            var result = await fixture.Coordinator(fake, maxNoProgress: 1).RunAsync(fixture.Context, token);
            Check("rechaza escritura antes de abrir sin modificar disco", !result.IsSuccess && !File.Exists(path));
            Check("rechazo conserva resultado de la tool call", fixture.Context.Messages.Any(m => m.ToolCallId == "write" && m.Content!.Contains("abre una transaccion")));
        }

        using (var fixture = new Fixture(root, "cierre"))
        {
            var path = Path.Combine(root, "cierre con espacios.txt");
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Open(), Call("write_text_file", new { path, content = "resultado=40" }, "write"))
                : Response(CheckCard(Receipt(request, "write_text_file")), Close(), Call("write_text_file", new { path, content = "alterado" }, "late"))));
            var engine = fixture.Coordinator(fake, criteria: [new(path, Contains: "40")]);
            var result = await engine.RunAsync(fixture.Context, token);
            Check("cierra con comprobacion y criterio de contenido", result.IsSuccess && fixture.Context.Ledger.GoalMet);
            Check("herramienta posterior al cierre no altera el artefacto", await File.ReadAllTextAsync(path, token) == "resultado=40"
                && fixture.Context.Messages.Any(m => m.ToolCallId == "late" && m.Content!.Contains("cerrada")));
            var journal = OperationEventJournal.Inspect(engine.JournalPath);
            Check("registro terminal sin acciones inciertas ni eventos sin confirmar", journal.Terminal && journal.UnresolvedActions.Count == 0 && journal.UnconfirmedEvents == 0);
            Check("resultados y eventos tienen identidad causal", fixture.Bus.History.All(e => e.RunId == fixture.Bus.RunId && e.EventId != Guid.Empty)
                && fixture.Bus.History.Any(e => e.Kind == AgentEventKind.HerramientaResultado && e.CausationId is not null));
        }

        using (var fixture = new Fixture(root, "falsa"))
        {
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Open(), CheckCard("inventado"), Close()) : Response(Abort())));
            var result = await fixture.Coordinator(fake).RunAsync(fixture.Context, token);
            Check("evidencia inventada no permite comprobar ni cerrar", !result.IsSuccess && !fixture.Context.Ledger.GoalMet && fixture.Context.Ledger.Rejected >= 2);
        }

        using (var fixture = new Fixture(root, "contenido"))
        {
            var path = Path.Combine(root, "contenido.txt");
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Open(), Call("write_text_file", new { path, content = "incorrecto" }, "write"))
                : Response(CheckCard(Receipt(request, "write_text_file")), Abort())));
            var result = await fixture.Coordinator(fake, criteria: [new(path, Contains: "correcto=40")]).RunAsync(fixture.Context, token);
            Check("archivo no vacio con contenido incorrecto es rechazado", !result.IsSuccess && fixture.Context.Ledger.Rejected == 1);
        }

        using (var fixture = new Fixture(root, "vigencia"))
        {
            var path = Path.Combine(root, "vigencia.txt");
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number switch
            {
                1 => Response(Open(), Call("write_text_file", new { path, content = "40" }, "write")),
                2 => Response(CheckCard(Receipt(request, "write_text_file")), Call("write_text_file", new { path, content = "41" }, "change"), Close()),
                _ => Response(Abort()),
            }));
            var result = await fixture.Coordinator(fake, criteria: [new(path)]).RunAsync(fixture.Context, token);
            Check("efecto posterior a comprobar invalida el cierre", !result.IsSuccess && fixture.Context.Ledger.Plays.Any(p => p.Name == "cerrar" && !p.Accepted));
        }

        using (var fixture = new Fixture(root, "control"))
        {
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new ScriptClient(async (request, number, ct) => { started.TrySetResult(); await release.Task.WaitAsync(ct); return Response(Open(), Abort()); });
            var engine = fixture.Coordinator(fake);
            var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var status = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var observer = fixture.Bus.Subscribe(e =>
            {
                if (e.Kind == AgentEventKind.Estado && e.Text.Contains("Paused")) status.TrySetResult();
                if (e.Kind == AgentEventKind.Estado) paused.TrySetResult();
            });
            var run = engine.RunAsync(fixture.Context, token);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
            engine.Control(AgentEventKind.Pausa);
            engine.Control(AgentEventKind.ConsultaEstado);
            await status.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
            Check("pausa y estado se atienden mientras el LLM espera", !run.IsCompleted && engine.Status == OperationStatus.Paused);
            engine.Control(AgentEventKind.Reanudacion);
            release.TrySetResult();
            await run.WaitAsync(TimeSpan.FromSeconds(3), token);
            Check("reanudacion permite completar la reaccion pendiente", fixture.Context.Ledger.Aborted);
        }

        using (var fixture = new Fixture(root, "deadline", seconds: 1))
        {
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancelled = false;
            var slow = new DelegateTool("slow", "accion lenta", "{\"type\":\"object\"}", async (_, ct) =>
            {
                began.TrySetResult();
                try { await Task.Delay(TimeSpan.FromSeconds(20), ct); }
                catch (OperationCanceledException) { cancelled = true; throw; }
                return "{}";
            });
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(Response(Open(), Call("slow", new { }, "slow"))));
            var engine = fixture.Coordinator(fake, extra: new([slow]));
            var result = await engine.RunAsync(fixture.Context, token).WaitAsync(TimeSpan.FromSeconds(4), token);
            Check("deadline cancela herramienta pendiente y clasifica el fallo", cancelled && result.Status == OperationStatus.Failed && fixture.Context.DeadlineReached);
            Check("cancelacion de herramienta conserva su resultado", fixture.Context.Messages.Any(m => m.ToolCallId == "slow" && m.Content!.Contains("error")));
        }

        using (var fixture = new Fixture(root, "cancel"))
        {
            var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new ScriptClient(async (request, number, ct) => { began.TrySetResult(); await Task.Delay(TimeSpan.FromSeconds(20), ct); return Response(); });
            var engine = fixture.Coordinator(fake);
            var run = engine.RunAsync(fixture.Context, token);
            await began.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
            engine.Control(AgentEventKind.Cancelacion);
            var result = await run.WaitAsync(TimeSpan.FromSeconds(3), token);
            Check("cancelacion del operador produce estado Cancelled y journal terminal", result.Status == OperationStatus.Cancelled && OperationEventJournal.Inspect(engine.JournalPath).Terminal);
        }

        using (var fixture = new Fixture(root, "observadores"))
        {
            var hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Bus.OnPublished = _ => hang.Task.GetAwaiter().GetResult();
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Open(), Call("add_numbers", new { a = 17, b = 23 }, "sum"))
                : Response(CheckCard(Receipt(request, "add_numbers")), Close())));
            var engine = fixture.Coordinator(fake, onTool: (_, _, _, _) => throw new Exception("observador defectuoso"));
            try
            {
                var result = await engine.RunAsync(fixture.Context, token).WaitAsync(TimeSpan.FromSeconds(3), token);
                Check("observadores rotos o lentos no bloquean efectos ni cierre", result.IsSuccess);
            }
            finally { hang.TrySetResult(); }
        }

        using (var fixture = new Fixture(root, "simular", mode: PolicyMode.Simular))
        {
            var path = Path.Combine(root, "simulado.txt");
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Open(), Call("write_text_file", new { path, content = "40" }, "write")) : Response(Abort())));
            await fixture.Coordinator(fake).RunAsync(fixture.Context, token);
            Check("politica simular no escribe ni genera evidencia ejecutada", !File.Exists(path)
                && fixture.Context.Messages.Any(m => m.ToolCallId == "write" && m.Content!.Contains("simulado")));
        }

        using (var fixture = new Fixture(root, "aprobar", mode: PolicyMode.Confirmar))
        {
            var path = Path.Combine(root, "aprobado.txt");
            var approval = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var subscription = fixture.Bus.Subscribe(e => { if (e.Kind == AgentEventKind.AprobacionSolicitada) approval.TrySetResult(); });
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Open(), Call("write_text_file", new { path, content = "40" }, "write")) : Response(Abort())));
            var engine = fixture.Coordinator(fake);
            var run = engine.RunAsync(fixture.Context, token);
            await approval.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
            Check("confirmacion espera un evento de aprobacion sin escribir", !File.Exists(path) && !run.IsCompleted);
            engine.Approve(true);
            await run.WaitAsync(TimeSpan.FromSeconds(3), token);
            Check("aprobacion explicita habilita el efecto pendiente", File.Exists(path));
        }

        using (var fixture = new Fixture(root, "silencio"))
        {
            var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new ScriptClient((request, number, ct) =>
            {
                if (number == 1) return Task.FromResult(Response(Open(), Call("lanzar", new
                { comando = OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 2" : "sleep 2" }, "background")));
                if (number == 2) { idle.TrySetResult(); return Task.FromResult(Response()); }
                return Task.FromResult(Response(Abort()));
            });
            var engine = fixture.Coordinator(fake);
            var run = engine.RunAsync(fixture.Context, token);
            await idle.Task.WaitAsync(TimeSpan.FromSeconds(4), token);
            await Task.Delay(250, token);
            Check("proceso silencioso no genera consultas periodicas al LLM", fixture.Context.Cycles == 2 && !run.IsCompleted);
            await run.WaitAsync(TimeSpan.FromSeconds(5), token);
            Check("fin real del proceso activa el handler del modelo", fixture.Context.Cycles == 3);
        }

        using (var fixture = new Fixture(root, "json"))
        {
            var path = Path.Combine(root, "criterio.json");
            var fake = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
                ? Response(Open(), Call("write_text_file", new { path, content = "texto no JSON" }, "write"))
                : number == 2 ? Response(CheckCard(Receipt(request, "write_text_file")), Close()) : Response(Abort())));
            var result = await fixture.Coordinator(fake, criteria: [new(path, ValidJson: true)]).RunAsync(fixture.Context, token);
            Check("contrato JSON rechaza contenido que solo es texto", !result.IsSuccess && fixture.Context.Ledger.Rejected >= 2);
        }

        var task = OperationContextBuilder.Build("ok, apruebo el plan, procede en modo de ejecucion",
            "sistema", [ChatMessage.FromUser("Crea un POS en pos_system con HTML JS y SQLite"),
                ChatMessage.FromAssistant("plan", reasoning: new string('x', 30000))]);
        Check("aprobacion hereda requisitos sin copiar razonamiento", task.Inherited && task.Objective.Contains("pos_system") && task.SessionContext == "sistema");
        var independent = OperationContextBuilder.Build("crea un poema nuevo", "sistema", [ChatMessage.FromUser("POS anterior")]);
        Check("objetivo independiente no hereda el proyecto anterior", !independent.Inherited && !independent.Objective.Contains("POS"));
        var bounded = OperationContextBuilder.Build("continua", new string('s', 20000),
            Enumerable.Range(0, 20).Select(_ => ChatMessage.FromUser(new string('u', 20000))).ToArray());
        Check("continuacion simple y acentuada hereda requisitos",
            bounded.Inherited && OperationContextBuilder.Build("contin\u00faa", "", [ChatMessage.FromUser("POS")]).Inherited);
        Check("contexto previo tiene limite independiente", bounded.Objective.Length < 7000 && bounded.SessionContext.Length < 6100);
        using (var fixture = new Fixture(root, "recuperacion POS"))
        {
            var path = Path.Combine(root, "pos.txt");
            var recoveryRequired = false;
            var observedAfterTrim = false;
            var fake = new ScriptClient((request, number, ct) =>
            {
                if (number == 1) return Task.FromResult(Response(Open()));
                if (number == 2) return Task.FromResult(JsonSerializer.SerializeToElement(new
                {
                    choices = new[] { new { finish_reason = "length", message = new { content = (string?)null, reasoning_content = new string('r', 2048) } } },
                    usage = new { completion_tokens = 2048, completion_tokens_details = new { reasoning_tokens = 2048 } },
                }));
                if (number == 3)
                {
                    recoveryRequired = request.ToolChoice == "required" && request.MaxTokens == 4096
                        && request.Thinking?.Mode == ThinkingMode.Disabled && request.Messages.All(m => m.ReasoningContent is null);
                    return Task.FromResult(Response(Call("write_text_file", new { path, content = "40" }, "write")));
                }
                if (number == 4)
                {
                    var receipt = Receipt(request, "write_text_file");
                    fixture.Context.Messages.Clear();
                    observedAfterTrim = fixture.Context.Brief([]).Contains(receipt);
                    return Task.FromResult(Response(CheckCard(receipt), Close()));
                }
                return Task.FromResult(Response(Abort()));
            });
            var result = await fixture.Coordinator(fake, criteria: [new(path, "40")]).RunAsync(fixture.Context, token);
            Check("razonamiento truncado recupera con accion pequena y presupuesto acotado", recoveryRequired
                && ResponseBudgets.DuringRecovery(50000, 4096) == 8192);
            Check("observacion y receipt sobreviven fuera del historial", observedAfterTrim);
            Check("recuperacion POS crea, comprueba y cierra artefacto real", result.IsSuccess && File.ReadAllText(path) == "40");
        }
        await ProviderContractSelfTest.RunAsync(Check, token);
        await AgentLoop.Security.SecuritySelfTest.RunAsync(Check, root, token);

        var scope = new Scope(new(), [], [Path.Combine(root, "permitido")], []);
        Check("scope no acepta directorio vecino por prefijo", !scope.AllowsPath(Path.Combine(root, "permitido-ajeno", "a.txt")));
        using (var fixture = new Fixture(root, "concurrencia", maxConcurrent: 1))
        {
            var first = await fixture.Supervisor.LaunchAsync(new ShellCommand(fixture.Supervisor.Policy.DefaultShell,
                OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 2" : "sleep 2"), token);
            var rejected = 0;
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
            {
                try { await fixture.Supervisor.LaunchAsync(new ShellCommand(fixture.Supervisor.Policy.DefaultShell, "echo extra"), token); }
                catch (InvalidOperationException) { Interlocked.Increment(ref rejected); }
            }));
            Check("reserva de capacidad evita superar MaxConcurrent", rejected == 8 && fixture.Supervisor.Running == 1);
            first.Kill();
            await first.Terminado.WaitAsync(TimeSpan.FromSeconds(3), token);
        }

        var journeyClient = new ScriptClient((request, number, ct) => Task.FromResult(number == 1
            ? Response(Open(), Call("add_numbers", new { a = 20, b = 20 }, "sum"))
            : Response(CheckCard(Receipt(request, "add_numbers")), Close())));
        var journeyRun = AgentLoop.Harness.Core.Run.FromObjective("suma 20 + 20", 10, "fixture");
        var (journeyResult, journey) = await new AgentLoop.Harness.Runner.GoalRunner(journeyClient).RunAsync(
            journeyRun, new() { WorkingDirectory = root }, token);
        Check("journey captura modelos y tools antes del resultado terminal", journeyResult.IsSuccess
            && journey.Steps.OfType<AgentLoop.Harness.Steps.ModelStep>().Count() == 2
            && journey.Steps.OfType<AgentLoop.Harness.Steps.ToolStep>().Count() == 4);

        var cancelledSignal = new AgentLoop.Concurrency.AsyncAutoResetEvent();
        for (var i = 0; i < 50; i++) await cancelledSignal.WaitAsync(TimeSpan.FromMilliseconds(1), token);
        Check("timeouts no retienen registros de esperadores", cancelledSignal.Waiters == 0);
        var unresolvedId = Guid.NewGuid();
        using (var journal = new OperationEventJournal(root, unresolvedId))
        {
            journal.Append("admitted", new AgentEvent(1, DateTimeOffset.UtcNow, AgentEventKind.HerramientaSolicitada, "fixture", "accion incierta",
                "{\"action_id\":\"uncertain\"}") { RunId = unresolvedId });
        }
        var inspection = OperationEventJournal.Inspect(Path.Combine(root, unresolvedId.ToString("N") + ".jsonl"));
        Check("recuperacion identifica efectos inciertos sin repetirlos", !inspection.Terminal && inspection.UnresolvedActions.SequenceEqual(["uncertain"]));

        Console.WriteLine("Self-test del coordinador por eventos:");
        foreach (var (name, ok) in results) Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        Console.WriteLine("Evidencia de fixtures: " + root);
        return results.All(r => r.Ok);
    }

    private static JsonElement Response(params object[] calls)
        => calls.Length == 0
            ? JsonSerializer.SerializeToElement(new { choices = new[] { new { finish_reason = "stop", message = new { role = "assistant", content = "texto sin accion" } } } })
            : JsonSerializer.SerializeToElement(new { choices = new[] { new { finish_reason = "tool_calls", message = new { role = "assistant", tool_calls = calls } } } });

    private sealed class ScriptClient(Func<ChatRequest, int, CancellationToken, Task<JsonElement>> response) : ILLMClient
    {
        private int _requests;
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => response(request, Interlocked.Increment(ref _requests), cancellationToken);
        public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.Yield(); yield break; }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _root;
        public EventBus Bus { get; }
        public ProcessSupervisor Supervisor { get; }
        public GoalContext Context { get; }
        public Scope Scope { get; }
        public Fixture(string root, string objective, int seconds = 10, PolicyMode mode = PolicyMode.Auto, int maxConcurrent = 4)
        {
            _root = root;
            Bus = new() { RunId = Guid.NewGuid(), DedupWindow = TimeSpan.Zero, AsyncObservers = true };
            var policy = new ExecutionPolicy { WorkingDirectory = root, Mode = mode, MaxConcurrent = maxConcurrent };
            Scope = Scope.Minimal(policy);
            Supervisor = new(Bus, policy);
            Context = new() { Goal = Goal.Of(objective, seconds), Bus = Bus, Supervisor = Supervisor,
                Ledger = new(CardDeck.Transaccional) { WorkingDirectory = root, AcceptanceObjective = objective } };
        }
        public EventOperationCoordinator Coordinator(ILLMClient client, int maxNoProgress = 3,
            IReadOnlyList<ArtifactCriterion>? criteria = null, ToolRegistry? extra = null,
            Action<string, string, bool, TimeSpan>? onTool = null)
        {
            var tools = OperationTools.Build(Context.Ledger, Supervisor, Bus).Combine(BuiltInTools.DefaultRegistry());
            if (extra is not null) tools = tools.Combine(extra);
            return new(client, "fixture", tools, Scope) { JournalDirectory = Path.Combine(_root, "journal"), MaxNoProgress = maxNoProgress, Criteria = criteria, OnToolCall = onTool };
        }
        public void Dispose() => Supervisor.Dispose();
    }
}
