using System.Diagnostics;
namespace AgentLoop.Goals;


/// <summary>
/// Self-test del watchdog del deadline.
///
/// Lo que se valida:
///   - Un goal con deadline de 1s y un cliente que se cuelga 10s termina
///     en menos de 5s con Fallido (no se queda esperando 10s).
///   - El DeadlineReached flag queda en true.
///   - El catch de cancelacion por usuario (externo) NO marca
///     DeadlineReached (es cancelacion, no timeout).
/// </summary>
public static class WatchdogSelfTest
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        // 1. Cliente que cuelga 10s: el watchdog del deadline debe cortarlo
        //    en ~1s (deadline = 1s), no esperar los 10s.
        var hangingClient = new HangingClient(delay: TimeSpan.FromSeconds(10));
        var policy = new ExecutionPolicy { WorkingDirectory = Path.GetFullPath(".") };
        var supervisor = new ProcessSupervisor(new EventBus(), policy);
        var ledger = new CardLedger(CardDeck.Transaccional, new EvidenceManifest());
        var tools = OperationTools.Build(ledger, supervisor, new EventBus());
        var machine = new LegacyEventDrivenAgent(hangingClient, "fake/model", tools);

        var loop = new Looping.AgentLoopRunner<ReactorState, GoalContext>(machine)
        {
            MaxIterations = 5,
            BreakWhen = _ => false,
        };

        var goalContext = new GoalContext
        {
            Goal = Goal.Of("watchdog test", seconds: 1),
            Bus = new EventBus(),
            Ledger = ledger,
            Supervisor = supervisor,
        };

        var sw = Stopwatch.StartNew();
        var result = loop.RunAsync(goalContext, CancellationToken.None).GetAwaiter().GetResult();
        sw.Stop();

        results.Add(("watchdog: el loop termina antes del hang del cliente", sw.Elapsed < TimeSpan.FromSeconds(5)));
        results.Add(("watchdog: el estado final es Fallido (el modelo no respondio)", result.FinalState == ReactorState.Fallido));
        results.Add(("watchdog: DeadlineReached = true", goalContext.DeadlineReached));
        results.Add(("watchdog: FailureReason menciona timeout",
            goalContext.FailureReason?.Contains("watchdog") == true || goalContext.FailureReason?.Contains("presupuesto") == true));

        // 2. Cancelacion del usuario (externo): NO marca DeadlineReached.
        var slowClient = new HangingClient(delay: TimeSpan.FromSeconds(10));
        var supervisor2 = new ProcessSupervisor(new EventBus(), policy);
        var ledger2 = new CardLedger(CardDeck.Transaccional, new EvidenceManifest());
        var tools2 = OperationTools.Build(ledger2, supervisor2, new EventBus());
        var machine2 = new LegacyEventDrivenAgent(slowClient, "fake/model", tools2);

        var loop2 = new Looping.AgentLoopRunner<ReactorState, GoalContext>(machine2)
        {
            MaxIterations = 5,
            BreakWhen = _ => false,
        };

        var goalContext2 = new GoalContext
        {
            Goal = Goal.Of("user cancel test", seconds: 60),
            Bus = new EventBus(),
            Ledger = ledger2,
            Supervisor = supervisor2,
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1));
        var result2 = loop2.RunAsync(goalContext2, cts.Token).GetAwaiter().GetResult();

        results.Add(("cancelacion externa: DeadlineReached = false", !goalContext2.DeadlineReached));

        supervisor.Dispose();
        supervisor2.Dispose();

        Console.WriteLine();
        Console.WriteLine("Self-test del watchdog:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }

    /// <summary>Cliente fake que cuelga <paramref name="delay"/> segundos antes de responder.</summary>
    private sealed class HangingClient : ILLMClient
    {
        private readonly TimeSpan _delay;

        public HangingClient(TimeSpan delay) => _delay = delay;

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public async Task<System.Text.Json.JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            var json = """{"id":"fake","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":""}}],"usage":{"prompt_tokens":0,"completion_tokens":0,"total_tokens":0}}""";
            return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone();
        }

        public async IAsyncEnumerable<System.Text.Json.JsonElement> StreamChatCompletionAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken).ConfigureAwait(false);
            yield break;
        }
    }
}
