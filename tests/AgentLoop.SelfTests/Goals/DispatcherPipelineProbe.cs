using System.Text.Json;
using CoreRun = AgentLoop.Harness.Core.Run;

namespace AgentLoop.Goals;


/// <summary>
/// Verifica directamente que el path fake-client -> parser ->
/// dispatcher -> ledger funciona para un caso simple. Sin reactor,
/// sin loop: el fake devuelve tool_calls, el reactor las extrae,
/// las ejecuta, el ledger.Play registra. Si el ledger no tiene
/// plays, el bug esta en ese path; si tiene, el bug esta en el loop
/// o en el reactor (que se prueba con selftest regular).
/// </summary>
public static class DispatcherPipelineProbe
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        var fakeClient = new ProbeClient();
        var run = CoreRun.FromObjective("dispatcher pipeline probe", 60);
        var policy = new Terminals.ExecutionPolicy { WorkingDirectory = Path.GetTempPath() };
        var scope = Harness.Scope.Minimal(policy);
        var setup = GoalRunFactory.BuildWithBusRecording(run, scope, fakeClient, Array.Empty<IRunObserver>());

        // Construimos el ParsedCompletion con una tool_call de tarjeta abrir
        // usando un Dict en lugar de un string JSON literal, que es mas robusto
        // ante escapes.
        var argsDict = new Dictionary<string, object>
        {
            ["nombre"] = "abrir",
            ["objetivo"] = "probe",
            ["plan"] = "test",
        };
        var argsJson = JsonSerializer.Serialize(argsDict);
        var toolCalls = new List<object>
        {
            new
            {
                id = "probe_0",
                type = "function",
                function = new
                {
                    name = "tarjeta",
                    arguments = argsJson,
                },
            },
        };
        var messageObj = new
        {
            role = "assistant",
            content = (string?)null,
            tool_calls = toolCalls,
        };
        var choices = new[] { new { index = 0, message = messageObj, finish_reason = "tool_calls" } };
        var usage = new { prompt_tokens = 10, completion_tokens = 10, total_tokens = 20 };
        var root = new { id = "probe", choices, usage };
        var raw = JsonDocument.Parse(JsonSerializer.Serialize(root)).RootElement.Clone();
        var parsed = AgentLoop.Parsing.CompletionParser.Parse(raw);

        results.Add(("parser extrajo tool_calls", parsed.ToolCalls.Count == 1));
        results.Add(("parser extrajo nombre=tarjeta", parsed.ToolCalls.Count > 0 && parsed.ToolCalls[0].Name == "tarjeta"));
        results.Add(("parser extrajo args como JSON parseado",
            parsed.ToolCalls.Count > 0 && parsed.ToolCalls[0].Arguments.ValueKind == JsonValueKind.Object));

        // Inyectamos el ParsedCompletion en el GoalContext como si fuera
        // una respuesta real del modelo.
        setup.GoalContext.Last = parsed;
        setup.GoalContext.Messages.Add(AgentLoop.Provider.ChatMessage.FromSystem("contrato probe"));
        setup.GoalContext.Messages.Add(AgentLoop.Provider.ChatMessage.FromUser("probe"));

        // Llamamos DispatchAsync manualmente. El reactor real es private;
        // usamos ToolRegistry directamente para verificar que el path
        // fake->parser->tool_registry->ledger funciona end-to-end.
        foreach (var call in parsed.ToolCalls)
        {
            var execution = setup.Tools.ExecuteAsync(call, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            results.Add(($"ejecutar tool: {execution.ToolName} (failed={execution.Failed})",
                execution.ToolName == "tarjeta"));
        }

        results.Add(("el ledger tiene plays despues del dispatch",
            setup.GoalContext.Ledger.Plays.Count > 0));
        results.Add(("la primera play es abrir",
            setup.GoalContext.Ledger.Plays.Count > 0
                && setup.GoalContext.Ledger.Plays[0].Name == "abrir"));
        results.Add(("la primera play fue aceptada",
            setup.GoalContext.Ledger.Plays.Count > 0
                && setup.GoalContext.Ledger.Plays[0].Accepted));
        results.Add(("hay System prompt en messages",
            setup.GoalContext.Messages.Any(m => m.Role == AgentLoop.Provider.ChatRoles.System)));
        results.Add(("hay User message",
            setup.GoalContext.Messages.Any(m => m.Role == AgentLoop.Provider.ChatRoles.User)));

        Console.WriteLine();
        Console.WriteLine("Self-test del pipeline del dispatcher:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }

    private sealed class ProbeClient : ILLMClient
    {
        public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

        public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(JsonDocument.Parse("{\"id\":\"probe\",\"choices\":[]}").RootElement.Clone());

        public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield break;
        }
    }
}
