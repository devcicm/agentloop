using System.Text.Json;
using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Goals;
using AgentLoop.Harness.Composition;
using AgentLoop.Harness.Observability;
using AgentLoop.Provider;
using CoreRun = AgentLoop.Harness.Core.Run;

namespace AgentLoop.Goals;

/// <summary>
/// Verifica el camino de recuperacion de truncamiento: si la primera
/// respuesta del modelo se corta por max_tokens (finish_reason=length, sin
/// tool calls), el reactor sube el presupuesto de tokens y le pide actuar.
/// El fake registra el MaxTokens de cada request: la segunda llamada debe
/// pedir mas tokens que la primera, y el ledger debe acabar con el abrir
/// que el modelo emite al recuperarse.
///
/// Sin red: el fake responde con una fixture distinta en cada llamada.
/// </summary>
public static class TruncationRecoverySelfTest
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        var fake = new TruncationFakeClient();
        var run = CoreRun.FromObjective("truncation recovery test", 60);
        var policy = new Terminals.ExecutionPolicy { WorkingDirectory = Path.GetTempPath() };
        var scope = Harness.Scope.Minimal(policy);
        var setup = GoalRunFactory.BuildWithBusRecording(run, scope, fake, Array.Empty<IRunObserver>());

        var machine = new EventDrivenAgent(fake, "fake/model", setup.Tools)
        {
            MaxTokens = 512,
        };

        var loop = new Looping.AgentLoopRunner<ReactorState, GoalContext>(machine)
        {
            MaxIterations = 10,
            BreakWhen = iteration => iteration.Context.Ledger.GoalMet || iteration.Context.Ledger.Aborted,
        };

        loop.RunAsync(setup.GoalContext, CancellationToken.None).GetAwaiter().GetResult();

        results.Add(("el fake recibio la primera llamada", fake.FirstMaxTokens > 0));
        results.Add(("el fake recibio la segunda llamada", fake.SecondMaxTokens > 0));
        results.Add(("la segunda llamada pidio mas tokens que la primera",
            fake.SecondMaxTokens > fake.FirstMaxTokens));
        results.Add(("el ledger registro el abrir tras la recuperacion",
            setup.GoalContext.Ledger.Plays.Any(p => p.Accepted && p.Name == "abrir")));
        results.Add(("el reactor publico el aviso de truncamiento",
            setup.Bus.History.Any(e => e.Text.Contains("trunco", StringComparison.OrdinalIgnoreCase))));

        setup.Supervisor.Dispose();

        Console.WriteLine();
        Console.WriteLine("Self-test de recuperacion de truncamiento:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }

    /// <summary>
    /// Primera llamada: finish_reason=length sin tool calls (truncado).
    /// Segunda: tool_calls con tarjeta abrir.
    /// Tercera en adelante: stop sin tool calls (fin).
    /// </summary>
    private sealed class TruncationFakeClient : ILLMClient
    {
        private int _calls;

        public int FirstMaxTokens { get; private set; }
        public int SecondMaxTokens { get; private set; }

        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            _calls++;
            var max = request.MaxTokens ?? 0;

            if (_calls == 1)
            {
                FirstMaxTokens = max;
                // Truncado: se quedo sin tokens razonando, sin tool calls.
                return Task.FromResult(Parse(
                    "{\"id\":\"t1\",\"choices\":[{\"index\":0,\"finish_reason\":\"length\",\"message\":{\"role\":\"assistant\",\"content\":null}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":"
                    + max
                    + ",\"total_tokens\":"
                    + (10 + max)
                    + "}}"));
            }

            if (_calls == 2)
            {
                SecondMaxTokens = max;
                var args = "{\\\"nombre\\\":\\\"abrir\\\",\\\"objetivo\\\":\\\"truncation\\\",\\\"plan\\\":\\\"test\\\"}";
                var toolCalls = "[{\"id\":\"t2_0\",\"type\":\"function\",\"function\":{\"name\":\"tarjeta\",\"arguments\":\"" + args + "\"}}]";
                return Task.FromResult(Parse(
                    "{\"id\":\"t2\",\"choices\":[{\"index\":0,\"finish_reason\":\"tool_calls\",\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":"
                    + toolCalls
                    + "}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":10,\"total_tokens\":20}}"));
            }

            return Task.FromResult(Parse(
                "{\"id\":\"t3\",\"choices\":[{\"index\":0,\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"ok\"}}],\"usage\":{\"prompt_tokens\":10,\"completion_tokens\":2,\"total_tokens\":12}}"));
        }

        public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }

        private static JsonElement Parse(string json)
            => JsonDocument.Parse(json).RootElement.Clone();
    }
}
