using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentLoop.Configuration;
using AgentLoop.Discovery;
using AgentLoop.Looping;
using AgentLoop.Parsing;
using AgentLoop.Provider;
using AgentLoop.StateMachines;
using AgentLoop.Tools;

namespace AgentLoop;

internal static class Program
{
    private const string ModelsArtifact = "artifacts/agentic-models.json";

    private static async Task<int> Main(string[] args)
    {
        if (ShouldRunInteractive(args))
        {
            return await InteractiveAsync();
        }

        var exitCode = await ExecuteAsync(args);
        PauseIfRequested(exitCode);
        return exitCode;
    }

    /// <summary>
    /// Visual Studio arranca el binario con los argumentos fijos del perfil, asi que no
    /// hay donde teclear un comando. Cuando no llegan argumentos y hay una consola de
    /// verdad detras, el CLI se ofrece como prompt en vez de imprimir la ayuda y morir.
    /// Con stdin redirigido (tuberias, CI) se mantiene el comportamiento de siempre.
    /// </summary>
    private static bool ShouldRunInteractive(string[] args)
    {
        if (args.Length == 1 && args[0].ToLowerInvariant() is "interactive" or "interactivo" or "repl")
        {
            return true;
        }

        return args.Length == 0 && !Console.IsInputRedirected;
    }

    /// <summary>Bucle de lectura: cada linea se trocea igual que una linea de terminal.</summary>
    private static async Task<int> InteractiveAsync()
    {
        Console.WriteLine("AgentLoop - CLI interactivo (el modo util al arrancar con F5 desde Visual Studio).");
        Console.WriteLine("Escribe el comando tal cual, sin el 'dotnet run --' delante.");
        Console.WriteLine("  selftest | chat \"hola\" | run \"Suma 17 y 23\" | discover --target 3");
        Console.WriteLine("Cualquier otro texto se manda al agente, como si fuera 'run <texto>'.");
        Console.WriteLine("'ayuda' lista todo, 'salir' cierra.");
        Console.WriteLine();

        var lastExitCode = 0;

        while (true)
        {
            Console.Write("agentloop> ");
            var line = Console.ReadLine();

            if (line is null)
            {
                // stdin cerrado: no queda nada que leer, salir es lo unico sensato.
                Console.WriteLine();
                return lastExitCode;
            }

            line = line.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.ToLowerInvariant() is "salir" or "exit" or "quit" or "q")
            {
                return lastExitCode;
            }

            var commandArgs = SplitArguments(line);
            if (commandArgs.Length == 0)
            {
                continue;
            }

            if (!IsKnownCommand(commandArgs[0]))
            {
                // Lo natural al teclear es hablarle al agente. La linea entera va como
                // un unico argumento para que no la troceen ni las comillas ni los espacios.
                commandArgs = ["run", line];
            }

            Console.WriteLine();
            lastExitCode = await ExecuteAsync(commandArgs);
            Console.WriteLine();
            Console.WriteLine($"[codigo de salida {lastExitCode}]");
            Console.WriteLine();
        }
    }

    private static bool IsKnownCommand(string token) => token.ToLowerInvariant() is
        "chat" or "discover" or "run" or "selftest"
        or "help" or "--help" or "-h" or "ayuda"
        or "interactive" or "interactivo" or "repl";

    /// <summary>
    /// Trocea una linea en argumentos respetando comillas dobles, para que
    /// run "Suma 17 y 23" llegue como un solo argumento igual que desde la shell.
    /// </summary>
    private static string[] SplitArguments(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true; // "" es un argumento vacio deliberado, no ruido
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(ch);
            hasToken = true;
        }

        if (hasToken)
        {
            tokens.Add(current.ToString());
        }

        return [.. tokens];
    }

    /// <summary>
    /// Con F5, Visual Studio cierra la ventana en cuanto termina la depuracion y el
    /// resultado se pierde. Solo se pausa si hay depurador enganchado: desde la terminal
    /// no lo hay y el proceso sale limpio, que es lo que necesitan tuberias y scripts.
    /// </summary>
    private static void PauseIfRequested(int exitCode)
    {
        if (!Debugger.IsAttached || Console.IsInputRedirected)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"[codigo de salida {exitCode}] Pulsa una tecla para cerrar...");
        Console.ReadKey(intercept: true);
    }

    private static async Task<int> ExecuteAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();

        // El modo interactivo llama aqui una vez por comando: el handler se
        // desengancha al salir para no acumular suscripciones muertas.
        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
            Console.WriteLine("\nCancelando...");
        }

        Console.CancelKeyPress += OnCancel;

        try
        {
            return await DispatchAsync(args, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("Interrumpido por el usuario.");
            return 130;
        }
        catch (TokenRouterException ex)
        {
            Console.Error.WriteLine($"ERROR del proveedor: {ex.Message}");
            if (ex.IsQuotaOrAccess)
            {
                Console.Error.WriteLine("La key no tiene acceso/saldo para ese modelo. Revisa https://tokenrouter.com/console/token");
            }

            return 1;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }

    private static async Task<int> DispatchAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

        return command switch
        {
            "chat" => await ChatAsync(args, cancellationToken),
            "discover" => await DiscoverAsync(args, cancellationToken),
            "run" => await RunAsync(args, cancellationToken),
            "selftest" => await SelfTestAsync(cancellationToken),
            "help" or "--help" or "-h" or "ayuda" => PrintHelp(),
            "interactive" or "interactivo" or "repl" => await InteractiveAsync(),
            _ => Fail($"Comando desconocido: '{command}'"),
        };
    }

    // ---------------------------------------------------------------- comandos

    /// <summary>Una sola llamada: ejercita CAPA 1 (HTTP) y CAPA 2 (parseo) por separado.</summary>
    private static async Task<int> ChatAsync(string[] args, CancellationToken cancellationToken)
    {
        var prompt = PositionalOrDefault(args, "Responde solo con la palabra OK.");
        using var client = BuildClient(out var source);
        var model = ResolveModel(args);

        Console.WriteLine($"key: {source}");
        Console.WriteLine($"modelo: {model}");
        Console.WriteLine($"prompt: {prompt}");
        Console.WriteLine();

        var raw = await client.CreateChatCompletionAsync(
            new ChatRequest
            {
                Model = model,
                Messages = [ChatMessage.FromUser(prompt)],
                MaxTokens = 512,
                Temperature = 0,
            },
            cancellationToken);

        var parsed = CompletionParser.Parse(raw);

        Console.WriteLine($"finish_reason : {parsed.FinishReason}");
        Console.WriteLine($"backend       : {parsed.Model}");
        Console.WriteLine($"usage         : {parsed.Usage}");
        if (parsed.Reasoning is not null)
        {
            Console.WriteLine($"reasoning     : {Shorten(parsed.Reasoning, 200)}");
        }

        Console.WriteLine();
        Console.WriteLine(parsed.Content ?? "(content vacio)");
        return 0;
    }

    /// <summary>Sondea el catalogo y reporta los modelos con capacidades agenticas.</summary>
    private static async Task<int> DiscoverAsync(string[] args, CancellationToken cancellationToken)
    {
        var target = IntOption(args, "--target", 3);
        var maxCandidates = IntOption(args, "--max-candidates", 30);

        using var client = BuildClient(out var source);
        Console.WriteLine($"key: {source}");
        Console.WriteLine($"Buscando {target} modelos con capacidades agenticas (accessible + chat + tool_calling)...");
        Console.WriteLine();

        var selector = new AgenticModelSelector(client, Console.WriteLine) { MaxCandidates = maxCandidates };
        var result = await selector.SelectAsync(target, cancellationToken);

        Console.WriteLine();
        Console.WriteLine($"Encontrados {result.Agentic.Count}/{target} tras sondear {result.AllProbed.Count} de {result.CandidatesConsidered} candidatos.");
        Console.WriteLine();

        if (result.Agentic.Count > 0)
        {
            Console.WriteLine($"{"MODELO",-42} {"BACKEND",-24} CAPACIDADES");
            Console.WriteLine(new string('-', 110));
            foreach (var report in result.Agentic)
            {
                Console.WriteLine($"{Shorten(report.ModelId, 41),-42} {Shorten(report.BackendModel, 23),-24} {string.Join(", ", report.SupportedNames)}");
            }
        }

        if (!result.ReachedTarget)
        {
            Console.WriteLine();
            Console.WriteLine($"AVISO: se pidieron {target} modelos agenticos y solo {result.Agentic.Count} lo son.");
            Console.WriteLine("Motivos de descarte:");
            foreach (var report in result.AllProbed.Where(r => !r.IsAgentic))
            {
                Console.WriteLine($"  {report.ModelId}: {report.SkipReason}");
            }
        }

        var path = Path.Combine(SecretStore.ResolveRepositoryRoot(), ModelsArtifact);
        await AgenticModelSelector.SaveAsync(result, path, cancellationToken);
        Console.WriteLine();
        Console.WriteLine($"Reporte completo: {path}");
        return result.ReachedTarget ? 0 : 2;
    }

    /// <summary>Corre el harness completo: maquina de estados + loop con bandera de corte.</summary>
    private static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var prompt = PositionalOrDefault(args, "Suma 17 y 23 usando la herramienta y dime el resultado.");
        var maxIterations = IntOption(args, "--max-iterations", 12);

        using var client = BuildClient(out var source);
        var model = ResolveModel(args);
        var tools = BuiltInTools.DefaultRegistry();

        Console.WriteLine($"key    : {source}");
        Console.WriteLine($"modelo : {model}");
        Console.WriteLine($"tools  : {string.Join(", ", tools.Names)}");
        Console.WriteLine($"prompt : {prompt}");
        Console.WriteLine();

        var machine = new AgentStateMachine(client, model, tools)
        {
            MaxTokens = 512,
            Temperature = 0,
            Trace = line => Console.WriteLine($"    {line}"),
        };

        var context = new AgentContext(
            prompt,
            systemPrompt: "Eres un agente conciso. Usa las herramientas disponibles cuando hagan falta y responde en espanol.");

        var loop = new AgentLoopRunner<AgentState, AgentContext>(machine)
        {
            MaxIterations = maxIterations,

            // La condicion que rompe el loop: en cuanto hay respuesta final, IsBroken pasa a true.
            BreakWhen = iteration => iteration.Context.FinalContent is not null,

            OnIteration = iteration =>
                Console.WriteLine($"[{iteration.Iteration,2}] {iteration.LastTransition}"),
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
        if (result.Error is not null)
        {
            Console.WriteLine($"Error       : {result.Error.Message}");
        }

        Console.WriteLine();
        Console.WriteLine("--- respuesta ----------------------------------------");
        Console.WriteLine(context.FinalContent ?? context.FailureReason ?? "(sin respuesta)");
        return result.Succeeded ? 0 : 1;
    }

    /// <summary>Verifica sin red que la bandera del loop corta por si sola.</summary>
    private static async Task<int> SelfTestAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Self-test sin red: maquina Tick/Tock que nunca termina.");
        Console.WriteLine();

        var byMax = new AgentLoopRunner<TickState, TickCounter>(new TickStateMachine()) { MaxIterations = 3 };
        var maxResult = await byMax.RunAsync(new TickCounter(), cancellationToken);
        Console.WriteLine($"  tope de iteraciones -> IsBroken={byMax.IsBroken} BreakReason={byMax.BreakReason} iteraciones={maxResult.Iterations}");

        var byCondition = new AgentLoopRunner<TickState, TickCounter>(new TickStateMachine())
        {
            MaxIterations = 100,
            BreakWhen = iteration => iteration.Context.Ticks >= 5,
        };
        var conditionResult = await byCondition.RunAsync(new TickCounter(), cancellationToken);
        Console.WriteLine($"  condicion booleana  -> IsBroken={byCondition.IsBroken} BreakReason={byCondition.BreakReason} iteraciones={conditionResult.Iterations}");

        var ok = maxResult is { BreakReason: LoopBreakReason.MaxIterations, Iterations: 3 } &&
                 conditionResult is { BreakReason: LoopBreakReason.ConditionMet, Iterations: 5 };

        Console.WriteLine();
        Console.WriteLine(ok ? "SELF-TEST OK" : "SELF-TEST FALLIDO");
        return ok ? 0 : 1;
    }

    // ---------------------------------------------------------------- soporte

    private static TokenRouterClient BuildClient(out string keySource)
    {
        if (!SecretStore.TryGetTokenRouterApiKey(out var apiKey, out var reason))
        {
            throw new InvalidOperationException(reason);
        }

        keySource = $"{SecretStore.Mask(apiKey!)} (desde {reason})";
        return new TokenRouterClient(new TokenRouterOptions { ApiKey = apiKey! });
    }

    /// <summary>--model &gt; primer modelo agentico descubierto &gt; fallback conocido.</summary>
    private static string ResolveModel(string[] args)
    {
        var explicitModel = StringOption(args, "--model");
        if (explicitModel is not null)
        {
            return explicitModel;
        }

        var path = Path.Combine(SecretStore.ResolveRepositoryRoot(), ModelsArtifact);
        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("agentic_models", out var models) &&
                    models.ValueKind == JsonValueKind.Array && models.GetArrayLength() > 0 &&
                    models[0].TryGetProperty("model_id", out var id) && id.GetString() is { Length: > 0 } modelId)
                {
                    return modelId;
                }
            }
            catch (JsonException)
            {
                // artefacto corrupto: se ignora y se usa el fallback
            }
        }

        return TokenRouterOptions.FallbackModelId;
    }

    /// <summary>
    /// Une todo lo posicional en un solo prompt. Sin esto, 'chat el clima de hoy' sin
    /// comillas llegaba al modelo como 'el' y la respuesta parecia culpa del LLM.
    /// Las opciones --x se saltan junto con su valor, vengan antes o despues del texto.
    /// </summary>
    private static string PositionalOrDefault(string[] args, string fallback)
    {
        var words = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    i++; // la opcion se lleva su valor: --model qwen/... no es prompt
                }

                continue;
            }

            words.Add(args[i]);
        }

        return words.Count == 0 ? fallback : string.Join(' ', words);
    }

    private static string? StringOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int IntOption(string[] args, string name, int fallback)
        => int.TryParse(StringOption(args, name), out var value) ? value : fallback;

    private static string Shorten(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        PrintHelp();
        return 64;
    }

    private static int PrintHelp()
    {
        Console.WriteLine(
            """
            AgentLoop - harness agentico minimo sobre TokenRouter

            Uso:
              dotnet run -- <comando> [opciones]
              dotnet run                          (sin argumentos: abre el CLI interactivo)

            Comandos:
              chat "<prompt>"       Una llamada suelta. Verifica capa de comunicacion + capa de parseo.
              discover              Sondea el catalogo y reporta 3 modelos con capacidades agenticas.
              run "<prompt>"        Corre la maquina de estados dentro del loop con tool calling.
              selftest              Prueba el corte del loop sin tocar la red.
              interactive           Abre un prompt para teclear comandos (lo que usa F5 en Visual Studio).

            Opciones:
              --model <id>          Modelo a usar (por defecto: el primero de artifacts/agentic-models.json).
              --target <n>          discover: cuantos modelos agenticos buscar (3).
              --max-candidates <n>  discover: cuantos modelos sondear como maximo (30).
              --max-iterations <n>  run: tope de iteraciones del loop (12).

            La API key se lee de local-secrets/tokenrouter-api-key.txt, o de la
            variable de entorno TOKENROUTER_API_KEY.
            """);
        return 0;
    }
}
