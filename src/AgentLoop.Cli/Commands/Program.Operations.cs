using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    /// <summary>La entrada principal del CLI publica un objetivo y ejecuta el reactor.</summary>
    internal static bool UsesConversationLoop(string verb) => verb == "run-chat";


    internal static async Task<int> AgentOperationAsync(
        ILLMClient client, ConversationSession thread, string objective,
        SessionSettings settings, int maxCycles, int deadline, CancellationToken cancellationToken)
    {
        if (maxCycles < 1 || deadline < 1) throw new InvalidOperationException("Los presupuestos deben ser positivos.");
        var policy = settings.Policy;
        var taskContext = OperationContextBuilder.Build(objective, "", thread.Messages);
        var run = Harness.Core.Run.FromObjective(taskContext.Objective, deadline, settings.Model);
        var renderer = settings.Stream ? new DeltaRenderer() : null;
        var consoleGate = new object();
        void Log(string text)
        {
            lock (consoleGate) { renderer?.Close(); ConsoleUi.Log(text); }
        }
        var setup = Harness.Composition.GoalRunFactory.BuildWithBusHook(
            run, Scope.Minimal(policy), client, [], item =>
            {
                lock (consoleGate) { Log($"  evento {item.Format()}"); ConsoleUi.Approval(item); }
            });
        using var supervisor = setup.Supervisor;
        var tools = setup.Tools;
        thread.Variables.WithDefaults(settings.Model, tools.Names);
        taskContext = taskContext with { SessionContext = OperationContextBuilder.LimitSessionContext(thread.RenderSystemPrompt()) };
        var context = new GoalContext
        {
            Goal = setup.GoalContext.Goal, Bus = setup.Bus, Ledger = setup.Ledger, Supervisor = supervisor,
            MaxHistoryCharacters = thread.MaxHistoryCharacters,
            SessionContext = taskContext.SessionContext,
        };
        var coordinator = new EventOperationCoordinator(client, settings.Model, tools, Scope.Minimal(policy), OperationServiceFactory.Create())
        {
            MaxModelCalls = maxCycles,
            MaxTokens = 2048, Thinking = settings.Thinking, Stream = settings.Stream,
            OnDelta = renderer is null ? null : delta => { lock (consoleGate) renderer.Render(delta); },
            OnToolCall = (name, output, success, _) => { lock (consoleGate) { renderer?.Close(); ConsoleUi.ToolResult(name, output, success); } },
            Trace = line => Log($"    {line}"),
        };
        ConsoleUi.Section("OPERACION  /  Objetivo por eventos");
        ConsoleUi.Field("Objetivo", objective);
        ConsoleUi.Field("Herramientas", string.Join(", ", tools.Names));
        settings.OnCoordinator?.Invoke(coordinator);
        EventOperationResult result;
        try { result = await coordinator.RunAsync(context, cancellationToken); }
        finally { settings.OnCoordinator?.Invoke(null); }
        ObserveOperationResult(result);
        renderer?.Close();
        var succeeded = context.Ledger.GoalMet && result.IsSuccess;
        var summary = (succeeded ? "CUMPLIDO: " : "SIN CERRAR: ")
            + (context.Ledger.Outcome ?? context.FailureReason ?? context.Ledger.PendingDebt() ?? result.Reason);
        var evidencePath = Path.Combine(SecretStore.ResolveRepositoryRoot(), ".agentloop", "operations",
            $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        AtomicFileWriter.Write(evidencePath, JsonSerializer.Serialize(new
        {
            objective, taskContext, model = settings.Model, succeeded, summary, context.Cycles, context.EventsSeen,
            state = result.Status.ToString(), stop = result.Reason, result.CorrelationId, failure = result.Outcome.Error, journal = coordinator.JournalPath,
            events = context.Bus.History,
            cards = context.Ledger.Plays.Select(p => new { p.Name, p.Fields, p.Accepted, p.Rejection, p.Depth, p.At }),
            messages = context.Messages,
            context.TotalUsage,
        }, new JsonSerializerOptions { WriteIndented = true }));
        // Se conserva el resultado de la operacion, no las tool_calls de otro libro mayor.
        var record = thread.StartTurn(objective);
        record.Messages.Add(ChatMessage.FromAssistant(summary + "\nEvidencia local: " + evidencePath));
        record.Turns = context.Cycles;
        record.TotalUsage = context.TotalUsage;
        thread.CommitTurn(record);
        Log(summary);
        Log(context.Summary());
        Log($"Evidencia: {evidencePath}");
        return succeeded ? 0 : 1;
    }


    /// <summary>
    /// Una operacion dirigida por eventos. No hay turnos: hay un objetivo, un mazo de
    /// tarjetas de estado y un supervisor de procesos. Los handlers aplican eventos y
    /// admiten acciones independientes; el cierre comprobado termina la operacion.
    /// </summary>
    private static async Task<int> GoalAsync(string[] args, CancellationToken cancellationToken)
    {
        var objective = DataGuard.Input(PositionalOrDefault(args, "Averigua que version de dotnet hay instalada y deja constancia."));
        var deadline = IntOption(args, "--deadline", 300);
        var maxCycles = IntOption(args, "--max-ciclos", 80);
        var thinking = ResolveThinkingPolicy(args);
        var stream = ResolveStreaming(args);

        ExecutionPolicy policy;
        try
        {
            policy = new ExecutionPolicy
            {
                Mode = ParsePolicyMode(StringOption(args, "--politica")),
                AllowShell = !HasFlag(args, "--no-shell"),
                AllowedPaths = StringOption(args, "--scope-root") is { } scopeRoot ? [Path.GetFullPath(scopeRoot)] : [],
                DefaultShell = ShellCommand.ParseShell(StringOption(args, "--shell")),
                WorkingDirectory = Path.GetFullPath(StringOption(args, "--cwd") ?? Environment.CurrentDirectory),
                MaxConcurrent = IntOption(args, "--max-procesos", 4),
                Confirm = ConfirmCommand,
            };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return Fail(ex.Message);
        }

        if (!Directory.Exists(policy.WorkingDirectory))
        {
            return Fail($"El directorio de trabajo no existe: {policy.WorkingDirectory}");
        }

        using var client = BuildClient(out var source, thinking);
        var model = ResolveModel(args);

        // Un solo renderer para todo: cierra el bloque abierto antes de cada linea de
        // traza o evento, para que lo que llega en vivo no se entrelace con el log.
        var renderer = stream ? new DeltaRenderer() : null;
        var console = new object();

        void Log(string line, ConsoleColor? color = null)
        {
            lock (console)
            {
                renderer?.Close();
                ConsoleUi.Log(line, color);
            }
        }

        // El setup completo lo construye la factory. Aqui solo inyectamos los
        // callbacks custom (Log para eventos y trazas, renderer para deltas) y
        // ejecutamos el coordinador con el contexto que la factory devolvio.
        var run = Harness.Core.Run.FromObjective(objective, deadline, model);
        var scope = new Scope(policy, [model], policy.AllowedPaths, []);
        EventOperationCoordinator? approvalTarget = null;
        var setup = Harness.Composition.GoalRunFactory.BuildWithBusHook(
            run, scope, client, [],
            item =>
            {
                Log($"  Ã‚Â· {item.Format()}", EventColor(item.Kind));
                if (item.Kind == AgentEventKind.AprobacionSolicitada)
                {
                    ConsoleUi.Approval(item);
                    using var request = JsonDocument.Parse(item.Data!);
                    var continuousAllowed = request.RootElement.GetProperty("continuous_allowed").GetBoolean();
                    ConsoleUi.Styled(ConsoleUi.Margin + (continuousAllowed ? "[s: una vez / c: continuo / N: rechazar] " : "[s: una vez / N: rechazar] "), ConsoleColor.Yellow);
                    var answer = Console.IsInputRedirected ? null : Console.ReadLine()?.Trim().ToLowerInvariant();
                    var continuous = continuousAllowed && answer == "c";
                    approvalTarget?.Approve(continuous || answer is "s" or "si", continuous,
                        request.RootElement.GetProperty("approval_id").GetString());
                }
            });
        using var supervisor = setup.Supervisor;

        ConsoleUi.Field("key", $"{source}");
        ConsoleUi.Field("modelo", $"{model}");
        ConsoleUi.Field("tools", $"{string.Join(", ", setup.Tools.Names)}");
        ConsoleUi.Field("tarjetas", $"{string.Join(", ", setup.Ledger.Deck.Names)} (cierra: {setup.Ledger.Deck.Final.Name})");
        ConsoleUi.Field("thinking", $"{DescribeThinking(thinking, model)}");
        ConsoleUi.Field("modo", $"{(stream ? "stream" : "respuesta completa")}");
        ConsoleUi.Field("politica", $"{policy}");
        ConsoleUi.Field("objetivo", $"{objective}");
        Console.WriteLine();

        var coordinator = new EventOperationCoordinator(client, model, setup.Tools, scope, OperationServiceFactory.Create())
        {
            Criteria = StringOption(args, "--artifact") is { } artifact
                ? [new ArtifactCriterion(artifact, StringOption(args, "--contains"), args.Contains("--json"), !args.Contains("--allow-existing"))]
                : null,
            MaxModelCalls = maxCycles,
            MaxTokens = IntOption(args, "--max-tokens", 2048),
            Temperature = 0,
            Thinking = thinking,
            Stream = stream,
            OnDelta = renderer is null ? null : renderer.Render,
            OnToolCall = (name, output, success, _) => { lock (console) { renderer?.Close(); ConsoleUi.ToolResult(name, output, success); } },
            Trace = line => Log($"    {line}"),
        };

        approvalTarget = coordinator;
        var operationResult = await coordinator.RunAsync(setup.GoalContext, cancellationToken).ConfigureAwait(false);
        ObserveOperationResult(operationResult);
        renderer?.Close();

        var ledger = setup.Ledger;
        var context = setup.GoalContext;

        Console.WriteLine();
        ConsoleUi.Section("OPERACION");
        ConsoleUi.Field("Coordinador", $"eventos y handlers");
        ConsoleUi.Field("Resultado", $"{operationResult.Reason}");
        ConsoleUi.Field("Estado final", $"{operationResult.Status}");
        ConsoleUi.Field("Registro", $"{coordinator.JournalPath}");
        ConsoleUi.Field("Resumen", $"{context.Summary()}");
        ConsoleUi.Field("Thinking", $"{(context.ThinkingObserved ? "observado" : "no observado")}");

        if (operationResult.Error is not null && !ledger.GoalMet)
        {
            ConsoleUi.Field("Error", operationResult.Outcome.Error?.Code);
        }

        Console.WriteLine();
        ConsoleUi.Section("LIBRO MAYOR");
        ConsoleUi.Text(ledger.DescribeState(), indent: 2);
        foreach (var play in ledger.Plays)
        {
            ConsoleUi.Log($"{play}");
        }

        Console.WriteLine();
        if (ledger.GoalMet && operationResult.IsSuccess)
        {
            ConsoleUi.Status($"CUMPLIDO: {ledger.Outcome}", true);
            return 0;
        }

        ConsoleUi.Status($"SIN CERRAR: {context.FailureReason ?? ledger.Outcome ?? ledger.PendingDebt() ?? "la tarjeta de cierre nunca se jugo"}", false);
        var faltante = ledger.RequiredEvidence();
        if (!ledger.GoalMet && faltante.Count > 0)
        {
            ConsoleUi.Text($"Evidencia faltante: {string.Join("; ", faltante)}", ConsoleColor.Yellow, 2);
        }

        return 1;
    }


    private static PolicyMode ParsePolicyMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" or "auto" => PolicyMode.Auto,
        "confirmar" or "confirm" => PolicyMode.Confirmar,
        "simular" or "dry" or "seco" => PolicyMode.Simular,
        var other => throw new InvalidOperationException($"Politica desconocida: {other}. Usa auto, confirmar o simular."),
    };


    /// <summary>Confirmacion por consola para --politica confirmar. Sin tty, se rechaza.</summary>
    private static bool ConfirmCommand(ShellCommand command)
    {
        if (Console.IsInputRedirected)
        {
            return false;
        }

        ConsoleUi.Section("APROBACION  /  Efecto pendiente");
        ConsoleUi.Text($"Ejecutar: {command}", ConsoleColor.Yellow);
        ConsoleUi.Styled(ConsoleUi.Margin + "[s/N] ", ConsoleColor.Yellow);

        var answer = Console.ReadLine()?.Trim().ToLowerInvariant();
        return answer is "s" or "si" or "y" or "yes";
    }


    private static ConsoleColor EventColor(AgentEventKind kind) => kind switch
    {
        AgentEventKind.Error or AgentEventKind.TarjetaRechazada => ConsoleColor.Red,
        AgentEventKind.Tarjeta => ConsoleColor.Green,
        AgentEventKind.Observador => ConsoleColor.Magenta,
        AgentEventKind.ProcesoIniciado or AgentEventKind.ProcesoFin => ConsoleColor.Cyan,
        AgentEventKind.Aviso => ConsoleColor.Yellow,
        _ => ConsoleColor.DarkGray,
    };

}
