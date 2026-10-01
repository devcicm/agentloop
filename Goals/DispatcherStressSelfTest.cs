using System.Collections.Concurrent;
using System.Text.Json;
using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Goals;
using AgentLoop.Harness.Composition;
using AgentLoop.Harness.Observability;
using AgentLoop.Harness.Steps;
using AgentLoop.Harness.Testing;
using AgentLoop.Provider;
using CoreRun = AgentLoop.Harness.Core.Run;
using ChatRoles = AgentLoop.Provider.ChatRoles;

namespace AgentLoop.Goals;

/// <summary>
/// Stress test del dispatcher: corre N harnesses completos (secuencial y
/// paralelo) y verifica que el pipeline entero termina consistente:
///   - El loop avanzo al menos un ciclo (no se quedo en Arranque).
///   - El primer mensaje es System (contrato del prompt de sistema).
///   - Hay mensajes Assistant (el modelo respondio).
///   - Cada ToolResult va precedido de un Assistant (sin tool results
///     huerfanos que harian rechazar el siguiente request al gateway).
///   - El bus no pierde eventos (History.Count == Published).
///   - El ledger avanzo: el modelo jugo "abrir" en algun momento.
///
/// Sin red ni disco (el FakeLLMClient no toca nada). El MultiToolFakeClient
/// devuelve una respuesta con N tool_calls por llamada: alterna "tarjeta"
/// (abrir en el primer batch, avanzar despues) y "ejecutar" (comando trivial
/// que retorna 0). Asi se ejercitan los dos caminos del dispatcher.
/// </summary>
public static class DispatcherStressSelfTest
{
    private const int NParallel = 4;
    private const int ToolCallsPerResponse = 3;

    // Iteraciones minimas para llegar a despachar: 1 Start (Arranque ->
    // Razonando), 2 Reason (Razonando -> Despachando), 3 Dispatch (ejecuta
    // las tools, mueve el ledger). Con 2 el loop rompe por MaxIterations
    // antes de DispatchAsync y el ledger queda vacio. 4 da margen para un
    // segundo ciclo de razonamiento.
    private const int Iterations = 4;

    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        // Caso secuencial: un solo harness.
        var fakeClient = new MultiToolFakeClient(toolCallsPerResponse: ToolCallsPerResponse);
        var sequential = RunSingleHarness(fakeClient, "#seq");
        lock (results)
        {
            results.AddRange(sequential);
        }
        results.Add(("cliente fake recibio al menos una request (secuencial)", fakeClient.TotalRequests >= 1));

        // Caso paralelo: N harnesses en paralelo contra el mismo cliente
        // (stateless: TotalRequests++ es la unica mutacion, y un long
        // increment es atomico; el contenido que devuelve es determinista).
        var errors = new ConcurrentBag<Exception>();
        var parallelResults = new ConcurrentBag<(string Name, bool Ok)>();
        Parallel.For(0, NParallel, i =>
        {
            try
            {
                foreach (var r in RunSingleHarness(fakeClient, $"#par{i}"))
                {
                    parallelResults.Add(r);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        });

        lock (results)
        {
            foreach (var r in parallelResults) results.Add(r);
        }

        results.Add(($"sin excepciones en paralelo ({NParallel} harnesses)", errors.IsEmpty));
        if (!errors.IsEmpty)
        {
            foreach (var e in errors.Take(3))
            {
                results.Add(($"excepcion paralela: {e.GetType().Name}", false));
            }
        }

        results.Add(("cliente fake recibio multiples requests (secuencial + paralelo)",
            fakeClient.TotalRequests >= 1 + NParallel));

        Console.WriteLine();
        Console.WriteLine("Self-test de stress del dispatcher:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }

    private static List<(string Name, bool Ok)> RunSingleHarness(
        MultiToolFakeClient fakeClient, string runIdSuffix)
    {
        var results = new List<(string Name, bool Ok)>();
        var localRun = CoreRun.FromObjective($"dispatcher stress test {runIdSuffix}", 60);
        var localPolicy = new Terminals.ExecutionPolicy { WorkingDirectory = Path.GetTempPath() };
        var localScope = Harness.Scope.Minimal(localPolicy);
        var localSetup = GoalRunFactory.BuildWithBusRecording(localRun, localScope, fakeClient, Array.Empty<IRunObserver>());

        var machine = new EventDrivenAgent(fakeClient, "fake/model", localSetup.Tools);

        var loop = new Looping.AgentLoopRunner<ReactorState, GoalContext>(machine)
        {
            MaxIterations = Iterations,
            BreakWhen = iteration =>
                iteration.Context.Ledger.GoalMet
                || iteration.Context.Ledger.Aborted
                || iteration.Context.Expired,
        };

        var result = loop.RunAsync(localSetup.GoalContext, CancellationToken.None).GetAwaiter().GetResult();

        var ctx = localSetup.GoalContext;

        // Un grupo de tool results cuelga de UN solo Assistant: el primero va
        // precedido del Assistant que pidio las tool_calls, y los siguientes de
        // otro tool. Es huerfano solo si el anterior no es Assistant ni Tool
        // (p. ej. un User en medio, que haria rechazar el request al gateway).
        var orphanTool = false;
        for (var i = 1; i < ctx.Messages.Count; i++)
        {
            if (ctx.Messages[i].Role == ChatRoles.Tool
                && ctx.Messages[i - 1].Role != ChatRoles.Assistant
                && ctx.Messages[i - 1].Role != ChatRoles.Tool)
            {
                orphanTool = true;
                break;
            }
        }

        results.Add(($"[h{runIdSuffix}] loop avanzo al menos un ciclo", result.Iterations > 0));
        results.Add(($"[h{runIdSuffix}] el primer mensaje es System (contrato)",
            ctx.Messages.Count > 0 && ctx.Messages[0].Role == ChatRoles.System));
        results.Add(($"[h{runIdSuffix}] hay mensajes Assistant",
            ctx.Messages.Any(m => m.Role == ChatRoles.Assistant)));
        results.Add(($"[h{runIdSuffix}] sin tool results huerfanos", !orphanTool));
        results.Add(($"[h{runIdSuffix}] el bus no perdio eventos",
            localSetup.Bus.History.Count == localSetup.Bus.Published));
        results.Add(($"[h{runIdSuffix}] hay plays en el ledger",
            ctx.Ledger.Plays.Count > 0));
        results.Add(($"[h{runIdSuffix}] el primer play es abrir aceptado",
            ctx.Ledger.Plays.Count > 0
                && ctx.Ledger.Plays[0].Accepted
                && ctx.Ledger.Plays[0].Name == "abrir"));

        localSetup.Supervisor.Dispose();
        return results;
    }

    /// <summary>
    /// Cliente fake que devuelve una respuesta con N tool_calls por llamada:
    /// la mitad son "ejecutar" (con un comando trivial que retorna 0),
    /// la otra mitad son "tarjeta" (la primera llamada del run es "abrir",
    /// las siguientes son "avanzar"). Esto ejercita el dispatcher con un
    /// batch realista de tool_calls heterogeneas.
    /// </summary>
    private sealed class MultiToolFakeClient : ILLMClient
    {
        private readonly int _toolCallsPerResponse;
        private long _totalRequests;

        public MultiToolFakeClient(int toolCallsPerResponse)
            => _toolCallsPerResponse = toolCallsPerResponse;

        public long TotalRequests => Interlocked.Read(ref _totalRequests);
        public int MaxToolCallsPerResponse { get; private set; }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _totalRequests);

            var toolCalls = new List<object>();
            for (var i = 0; i < _toolCallsPerResponse; i++)
            {
                // Primer tool_call siempre tarjeta abrir (el modelo emite esto en cada
                // llamada porque cada harness arranca con un ledger vacio; los harnesses
                // no comparten ledger ni contexto, asi que reabrir es legal). El resto
                // alterna ejecutar / tarjeta avanzar para ejercitar el dispatcher con
                // un batch heterogeneo. El fake es stateless: el ledger decide que es
                // legal o no, no el fake.
                var isTarjeta = i == 0 || i % 2 == 0;
                var name = isTarjeta && i == 0
                    ? "tarjeta"
                    : isTarjeta ? "tarjeta" : "ejecutar";
                var args = isTarjeta && i == 0
                    ? "{\"nombre\":\"abrir\",\"objetivo\":\"stress\",\"plan\":\"test\"}"
                    : isTarjeta
                        ? "{\"nombre\":\"avanzar\",\"paso\":\"stress step\"}"
                        : "{\"comando\":\"echo ok\"}";
                toolCalls.Add(new
                {
                    id = $"call_{TotalRequests}_{i}",
                    type = "function",
                    function = new { name, arguments = args },
                });
            }

            if (ToolCallsPerResponse > MaxToolCallsPerResponse)
            {
                MaxToolCallsPerResponse = ToolCallsPerResponse;
            }

            // Formato OpenAI estandar: tool_calls es un campo al nivel del message,
            // no dentro de content. ExtractToolCalls del parser busca ahi.
            var messageJson = "{\"role\":\"assistant\",\"content\":null,\"tool_calls\":"
                + JsonSerializer.Serialize(toolCalls)
                + "}";
            var json = "{\"id\":\"stress\",\"choices\":[{\"index\":0,\"finish_reason\":\"tool_calls\",\"message\":"
                + messageJson
                + "}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":10,\"total_tokens\":20}}";

            return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
        }

        public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
