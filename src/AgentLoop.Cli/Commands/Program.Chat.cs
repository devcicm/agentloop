using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    /// <summary>Un intercambio del usuario, con el hilo y los parametros de la sesion puestos.</summary>
    private static async Task<int> AgentTurnAsync(
        ILLMClient client,
        ConversationSession thread,
        string prompt,
        ToolRegistry tools,
        SessionSettings settings,
        int maxIterations,
        CancellationToken cancellationToken)
    {
        var model = settings.Model;
        tools = tools.WithScope(Scope.Minimal(settings.Policy));

        // Se refrescan aqui para que {{hora}} sea la del turno, no la del arranque.
        thread.Variables.WithDefaults(model, tools.Names);

        // El renderer se comparte entre las llamadas del turno: cierra el bloque abierto
        // antes de cada traza para que el texto en vivo no se mezcle con el log del loop.
        var renderer = settings.Stream ? new DeltaRenderer() : null;

        var machine = new AgentStateMachine(client, model, tools)
        {
            MaxTokens = 512,
            Temperature = 0,
            Thinking = settings.Thinking,
            Stream = settings.Stream,
            OnDelta = renderer is null ? null : renderer.Render,
            Trace = line =>
            {
                renderer?.Close();
                Console.WriteLine($"    {line}");
            },
        };

        var context = thread.StartTurn(prompt);

        var loop = new AgentLoopRunner<AgentState, ChatTurnContext>(machine)
        {
            MaxIterations = maxIterations,
            BreakWhen = iteration => iteration.Context.FinalContent is not null,
            OnIteration = iteration =>
            {
                renderer?.Close();
                Console.WriteLine($"[{iteration.Iteration,2}] {iteration.LastTransition}");
            },
        };

        var result = await loop.RunAsync(context, cancellationToken);

        Console.WriteLine();

        if (result.Result.IsOk)
        {
            thread.CommitTurn(context);
        }
        else
        {
            // Un turno a medias puede dejar un assistant con tool_calls sin sus resultados,
            // y el gateway rechaza esa conversacion en la siguiente peticion. Se descarta.
            Console.WriteLine("(turno no completado: se descarta del hilo para no corromperlo)");
        }

        ConsoleUi.Section("RESPUESTA");
        ConsoleUi.Text(context.FinalContent ?? context.FailureReason ?? "(sin respuesta)");
        Console.WriteLine();

        if (context.ThinkingObserved)
        {
            Console.WriteLine($"[thinking: {context.LastThinking}]");
        }

        Console.WriteLine($"[hilo: {thread.Messages.Count} mensajes | intercambios {thread.Exchanges} | turnos LLM {thread.Turns} | {thread.TotalUsage}]");

        return result.Result.IsOk ? 0 : 1;
    }


    // ---------------------------------------------------------------- comandos

    /// <summary>Una sola llamada: ejercita CAPA 1 (HTTP) y CAPA 2 (parseo) por separado.</summary>
    private static async Task<int> ChatAsync(string[] args, CancellationToken cancellationToken)
    {
        var prompt = DataGuard.Input(PositionalOrDefault(args, "Responde solo con la palabra OK."));
        var policy = ResolveThinkingPolicy(args);
        var stream = ResolveStreaming(args);
        using var client = BuildClient(out var source, policy);
        var model = ResolveModel(args);

        var thread = new ConversationSession { SessionStore = new FileConversationSessionStore() };
        ApplyPromptOptions(thread, args);
        thread.Variables.WithDefaults(model, []);

        ConsoleUi.Section("CHAT  /  Solicitud al proveedor");
        ConsoleUi.Field("key", $"{source}");
        ConsoleUi.Field("modelo", $"{model}");
        ConsoleUi.Field("thinking", $"{DescribeThinking(policy, model)}");
        ConsoleUi.Field("modo", $"{(stream ? "stream" : "respuesta completa")}");
        ConsoleUi.Field("prompt", $"{prompt}");
        Console.WriteLine();

        var request = new ChatRequest
        {
            Model = model,
            Messages =
            [
                ChatMessage.FromSystem(thread.RenderSystemPrompt()),
                ChatMessage.FromUser(TextSanitizer.Plain(prompt)),
            ],
            // El razonamiento se paga del mismo max_tokens, asi que se deja margen.
            MaxTokens = policy.EnsureHeadroom(512, model),
            Temperature = 0,
            Thinking = policy.For(model),

            // Sin include_usage el stream no trae el consumo y no habria como contarlo.
            StreamOptions = stream ? StreamOptions.WithUsage : null,
        };

        ParsedCompletion parsed;
        var streamNote = string.Empty;

        if (stream)
        {
            var renderer = new DeltaRenderer();
            var accumulator = new StreamAccumulator();

            await foreach (var chunk in client.StreamChatCompletionAsync(request, cancellationToken))
            {
                var delta = accumulator.Add(chunk);
                if (!delta.IsEmpty)
                {
                    renderer.Render(delta);
                }
            }

            renderer.Close();
            Console.WriteLine();

            streamNote = $"{accumulator.Chunks} chunks, {accumulator.Deltas} con contenido" +
                         (accumulator.HasUsage ? string.Empty : ", sin usage (el backend no lo manda)");

            // El acumulador rearma la respuesta completa: de aqui en adelante da igual
            // por que camino llego.
            parsed = accumulator.Build();
        }
        else
        {
            parsed = CompletionParser.Parse(await client.CreateChatCompletionAsync(request, cancellationToken));
        }

        ConsoleUi.Section("METRICAS  /  Respuesta del proveedor");
        ConsoleUi.Field("Finalizacion", parsed.FinishReason);
        ConsoleUi.Field("Modelo", parsed.Model);
        ConsoleUi.Field("Tokens", parsed.Usage);
        ConsoleUi.Field("Razonamiento", parsed.Thinking);
        if (!stream && parsed.Thinking.Text is { Length: > 0 } thought)
        {
            ConsoleUi.Text(Shorten(thought, 200), ConsoleColor.DarkGray, 2);
        }

        if (stream)
        {
            ConsoleUi.Field("Stream", streamNote);
            return 0;
        }

        Console.WriteLine();
        ConsoleUi.Section("RESPUESTA");
        ConsoleUi.Text(parsed.Content ?? "(content vacio)");
        return 0;
    }


    /// <summary>Corre el harness completo: maquina de estados + loop con bandera de corte.</summary>
    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var prompt = DataGuard.Input(PositionalOrDefault(args, "Suma 17 y 23 usando la herramienta y dime el resultado."));
        var maxIterations = IntOption(args, "--max-iterations", 12);
        var policy = ResolveThinkingPolicy(args);
        var stream = ResolveStreaming(args);

        using var client = BuildClient(out var source, policy);
        var model = ResolveModel(args);
        var tools = BuiltInTools.DefaultRegistry().WithScope(Scope.Minimal(new ExecutionPolicy
        {
            AllowShell = !HasFlag(args, "--no-shell"),
            AllowedPaths = StringOption(args, "--scope-root") is { } scopeRoot ? [Path.GetFullPath(scopeRoot)] : [],
            WorkingDirectory = Path.GetFullPath(StringOption(args, "--cwd") ?? Environment.CurrentDirectory),
        }));

        ConsoleUi.Section("CHAT  /  Solicitud al proveedor");
        ConsoleUi.Field("key", $"{source}");
        ConsoleUi.Field("modelo", $"{model}");
        ConsoleUi.Field("tools", $"{string.Join(", ", tools.Names)}");
        ConsoleUi.Field("thinking", $"{DescribeThinking(policy, model)}");
        ConsoleUi.Field("modo", $"{(stream ? "stream" : "respuesta completa")}");
        ConsoleUi.Field("prompt", $"{prompt}");
        Console.WriteLine();

        // Un solo renderer para todo el loop: cierra el bloque abierto antes de cada traza
        // para que lo que llega en vivo no se entrelace con el log de iteraciones.
        var renderer = stream ? new DeltaRenderer() : null;

        var machine = new AgentStateMachine(client, model, tools)
        {
            MaxTokens = 512,
            Temperature = 0,
            Thinking = policy,
            Stream = stream,
            OnDelta = renderer is null ? null : renderer.Render,
            Trace = line =>
            {
                renderer?.Close();
                Console.WriteLine($"    {line}");
            },
        };

        var thread = new ConversationSession { SessionStore = new FileConversationSessionStore() };
        ApplyPromptOptions(thread, args);
        thread.Variables.WithDefaults(model, tools.Names);

        var context = thread.StartTurn(prompt);

        var loop = new AgentLoopRunner<AgentState, ChatTurnContext>(machine)
        {
            MaxIterations = maxIterations,

            // La condicion que rompe el loop: en cuanto hay respuesta final, IsBroken pasa a true.
            BreakWhen = iteration => iteration.Context.FinalContent is not null,

            OnIteration = iteration =>
            {
                renderer?.Close();
                Console.WriteLine($"[{iteration.Iteration,2}] {iteration.LastTransition}");
            },
        };

        var result = await loop.RunAsync(context, cancellationToken);

        Console.WriteLine();
        Console.WriteLine("--- loop ---------------------------------------------");
        Console.WriteLine($"IsBroken    : {loop.IsBroken}");
        Console.WriteLine($"BreakReason : {loop.BreakReason}");
        Console.WriteLine($"Iteraciones : {result.Iterations}/{maxIterations}");
        Console.WriteLine($"Estado final: {result.FinalState}");
        Console.WriteLine($"Turnos LLM  : {context.Turns}");
        Console.WriteLine($"Tools       : {context.ToolExecutions.Count} ejecutadas");
        Console.WriteLine($"Tokens      : {context.TotalUsage}");
        Console.WriteLine($"Thinking    : {(context.ThinkingObserved ? context.LastThinking.ToString() : "no observado")}");
        if (result.Error is not null)
        {
            Console.WriteLine($"Error       : {result.Error.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("--- respuesta ----------------------------------------");
        ConsoleUi.Section("RESPUESTA");
        ConsoleUi.Text(context.FinalContent ?? context.FailureReason ?? "(sin respuesta)");
        return result.Result.IsOk ? 0 : 1;
    }

}
