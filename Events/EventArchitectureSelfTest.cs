using System.Text.Json;
using AgentLoop.Cards;
using AgentLoop.Concurrency;
using AgentLoop.Goals;
using AgentLoop.Provider;
using AgentLoop.Terminals;
using AgentLoop.Tools;
using AgentLoop.StateMachines;

namespace AgentLoop.Events;

/// <summary>Contratos de activacion del reactor, sin proveedor remoto.</summary>
public static class EventArchitectureSelfTest
{
    public static async Task<bool> RunAsync(CancellationToken token = default)
    {
        var results = new List<(string Name, bool Ok)>();
        void Check(string name, bool ok) => results.Add((name, ok));

        var signal = new AsyncAutoResetEvent();
        using (var cancelled = new CancellationTokenSource())
        {
            var oldWait = signal.WaitAsync(cancelled.Token);
            cancelled.Cancel();
            try { await oldWait; } catch (OperationCanceledException) { }
            var newWait = signal.WaitAsync(TimeSpan.FromMilliseconds(150), token);
            signal.Set();
            Check("un esperador cancelado no roba la siguiente senal", await newWait);
        }

        var bus = new EventBus { DedupWindow = TimeSpan.Zero };
        bus.Publish(AgentEventKind.Aviso, "test", "ya consumido");
        bus.Drain();
        Check("drenar la cola no deja un despertar fantasma", !await bus.WaitAsync(TimeSpan.FromMilliseconds(80), token));
        var waiting = bus.WaitAsync(TimeSpan.FromSeconds(2), token);
        bus.Publish(AgentEventKind.Aviso, "test", "nuevo");
        Check("publicar despierta al consumidor y conserva el evento", await waiting && bus.Drain().Count == 1);

        var observed = new EventBus { OnPublished = _ => throw new InvalidOperationException("observador roto") };
        var observerWait = observed.WaitAsync(TimeSpan.FromMilliseconds(150), token);
        var published = true;
        try { observed.Publish(AgentEventKind.Aviso, "test", "hecho"); }
        catch (InvalidOperationException) { published = false; }
        Check("un observador roto no rompe publicacion ni despertar", published && await observerWait);

        var parallel = new EventBus { DedupWindow = TimeSpan.Zero, MaxHistory = 1000 };
        Parallel.For(0, 500, i => parallel.Publish(AgentEventKind.Aviso, "parallel", i.ToString()));
        var batch = parallel.Drain(1000);
        Check("500 publicaciones concurrentes sin perdida ni duplicados", batch.Count == 500 && batch.Select(e => e.Sequence).Distinct().Count() == 500);
        for (var i = 0; i < 5; i++) parallel.Publish(AgentEventKind.Aviso, "batch", i.ToString());
        parallel.Drain(2);
        Check("un lote parcial deja eventos disponibles sin esperar otra senal", await parallel.WaitAsync(TimeSpan.Zero, token) && parallel.Drain().Count == 3);

        var reactorBus = new EventBus { DedupWindow = TimeSpan.Zero };
        using var supervisor = new ProcessSupervisor(reactorBus, new ExecutionPolicy { WorkingDirectory = Environment.CurrentDirectory });
        var context = new GoalContext { Goal = Goal.Of("validar eventos", 20), Bus = reactorBus,
            Supervisor = supervisor, Ledger = new CardLedger(CardDeck.Transaccional) };
        var client = new RecordingClient();
        var reactor = new EventDrivenAgent(client, "fixture", BuiltInTools.DefaultRegistry());
        await reactor.StepAsync(context, token); // Publica objetivo.
        reactorBus.Drain(); // Simula que otro consumidor ya retiro el lote.
        await reactor.StepAsync(context, token);
        Check("sin eventos no hay llamada al LLM", client.Requests == 0 && reactor.CurrentState == ReactorState.Esperando);

        reactorBus.Publish(AgentEventKind.Aviso, "test", "consulta hora");
        reactor.Reset(ReactorState.Razonando);
        client.Tool = true;
        await reactor.StepAsync(context, token);
        await reactor.StepAsync(context, token);
        Check("el resultado de una herramienta publica un evento inmediato", reactorBus.History.Any(e => e.Source == "dispatcher" && e.Text.Contains("get_time")));
        Check("resultado de herramienta sin cierre no cumple objetivo", !context.Ledger.GoalMet && !reactor.IsFinished);

        reactorBus.Drain();
        reactorBus.Publish(AgentEventKind.Aviso, "test", "evento inicial");
        client.OnRequest = () => reactorBus.Publish(AgentEventKind.Observador, "test", "durante razonamiento");
        reactor.Reset(ReactorState.Razonando);
        await reactor.StepAsync(context, token);
        Check("eventos durante la llamada al LLM quedan pendientes", reactorBus.Drain().Any(e => e.Text == "durante razonamiento"));
        await reactor.StepAsync(context, token); // Completa la herramienta pendiente.
        client.OnRequest = null;

        client.Tool = false;
        var command = OperatingSystem.IsWindows() ? "Start-Sleep -Seconds 2; Write-Output evento_fin" : "sleep 2; echo evento_fin";
        var watcher = supervisor.Watch("evento_fin");
        var watcherEvent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processEnd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reactorBus.OnPublished = item =>
        {
            if (item.Kind == AgentEventKind.Observador) watcherEvent.TrySetResult();
            if (item.Kind == AgentEventKind.ProcesoFin) processEnd.TrySetResult();
        };
        await supervisor.LaunchAsync(new ShellCommand(supervisor.Policy.DefaultShell, command), token);
        reactorBus.Drain();
        reactor.Reset(ReactorState.Esperando);
        var callsBefore = client.Requests;
        var asleep = reactor.StepAsync(context, token).AsTask();
        await Task.Delay(100, token);
        Check("proceso vivo silencioso: espera asincrona sin consultar LLM", !asleep.IsCompleted && client.Requests == callsBefore);
        await asleep.WaitAsync(TimeSpan.FromSeconds(8), token);
        await reactor.StepAsync(context, token);
        Check("salida del proceso despierta y causa una reaccion", client.Requests == callsBefore + 1 && context.EventsSeen > 0);
        Check("texto final del LLM no sustituye tarjeta de cierre", !context.Ledger.GoalMet && !reactor.IsFinished);
        await watcherEvent.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        Check("patron de salida publica Observador", watcher.Fired && reactorBus.History.Any(e => e.Kind == AgentEventKind.Observador));
        await processEnd.Task.WaitAsync(TimeSpan.FromSeconds(3), token);
        Check("fin del proceso publica ProcesoFin", supervisor.Running == 0 && reactorBus.History.Any(e => e.Kind == AgentEventKind.ProcesoFin));

        Check("CLI: texto libre y run usan reactor; solo chat usa conversacion",
            !Program.UsesConversationLoop("run") && !Program.UsesConversationLoop("crea") && Program.UsesConversationLoop("chat"));
        var thread = new ConversationThread();
        var previous = new AgentContext("antecedente", "");
        previous.Messages.Add(ChatMessage.FromAssistant("marca_contexto_eventos"));
        thread.CommitTurn(previous);
        var closing = new ClosingClient();
        var code = await Program.AgentOperationAsync(closing, thread, "Suma 17 y 23 con herramienta y comprueba el resultado",
            new Program.SessionSettings { Model = "fixture/eventos", Thinking = new ThinkingPolicy(ThinkingPreference.Never), Stream = false }, 6, 20, token);
        Check("entrada principal CLI completa objetivo por tarjetas", code == 0 && closing.Requests == 3);
        Check("CLI transmite antecedentes y herramientas operativas", closing.ContextReceived && closing.ToolsReceived);
        Check("CLI conserva resultado y ruta de evidencia local", thread.Exchanges == 2 && thread.Messages.Last().Content!.Contains("Evidencia local:"));

        Console.WriteLine("Self-test de arquitectura por eventos:");
        foreach (var (name, ok) in results) Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        return results.All(r => r.Ok);
    }

    private sealed class RecordingClient : ITokenRouterClient
    {
        public int Requests { get; private set; }
        public bool Tool { get; set; }
        public Action? OnRequest { get; set; }
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            OnRequest?.Invoke();
            var json = Tool
                ? """{"choices":[{"finish_reason":"tool_calls","message":{"role":"assistant","tool_calls":[{"id":"hora","type":"function","function":{"name":"get_time","arguments":"{}"}}]}}]}"""
                : """{"choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"Terminado"}}]}""";
            using var document = JsonDocument.Parse(json);
            return Task.FromResult(document.RootElement.Clone());
        }
        public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.Yield(); yield break; }
    }

    private sealed class ClosingClient : ITokenRouterClient
    {
        public int Requests { get; private set; }
        public bool ContextReceived { get; private set; }
        public bool ToolsReceived { get; private set; }
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
        public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            Requests++;
            ContextReceived |= request.Messages[0].Content!.Contains("marca_contexto_eventos");
            ToolsReceived |= new[] { "tarjeta", "ejecutar", "lanzar", "where_command", "find_files", "add_numbers" }
                .All(name => request.Tools!.Any(t => t.Function.Name == name));
            if (request.Messages.Last().Content?.Contains("NOVEDADES (") != true)
                throw new InvalidOperationException("Llamada al modelo sin parte de eventos");
            object Call(string name, object args, string id) => new { id, type = "function", function = new { name, arguments = JsonSerializer.Serialize(args) } };
            object[] calls;
            if (Requests == 1)
                calls = [Call("tarjeta", new { nombre = "abrir", objetivo = "sumar", plan = "usar add_numbers y comprobar" }, "abrir"),
                    Call("add_numbers", new { a = 17, b = 23 }, "suma")];
            else
            {
                if (!request.Messages.Any(m => m.Role == ChatRoles.Tool && m.Name == "add_numbers" && m.Content!.Contains("40")))
                    throw new InvalidOperationException("No se recibio el resultado real de sumar");
                calls = [Call("tarjeta", new { nombre = Requests == 2 ? "comprobar" : "cerrar",
                    comprobacion = "resultado de add_numbers", resultado = "40", evidencia = "add_numbers devolvio sum=40" }, "card" + Requests)];
            }
            return Task.FromResult(JsonSerializer.SerializeToElement(new { choices = new[] {
                new { finish_reason = "tool_calls", message = new { role = "assistant", tool_calls = calls } } } }));
        }
        public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        { await Task.Yield(); yield break; }
    }
}
