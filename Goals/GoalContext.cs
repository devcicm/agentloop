using System.Text;
using System.Text.Json;
using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Parsing;
using AgentLoop.Prompting;
using AgentLoop.Provider;
using AgentLoop.Terminals;
using AgentLoop.Security;

namespace AgentLoop.Goals;

/// <summary>
/// Lo que se persigue y con que presupuesto. El objetivo no se da por cumplido por tiempo
/// ni por numero de pasos: solo por la tarjeta de cierre, que es lo unico que el sistema
/// acepta como final.
/// </summary>
public sealed record Goal(string Text, TimeSpan Budget)
{
    public static Goal Of(string text, int seconds = 300) => new(text.Trim(), TimeSpan.FromSeconds(seconds));
}

/// <summary>Estado vivo de una operacion dirigida por eventos.</summary>
public sealed class GoalContext
{
    public required Goal Goal { get; init; }
    public Projects.ProjectRuntime? Project { get; init; }

    public required EventBus Bus { get; init; }

    public required CardLedger Ledger { get; init; }

    public required ProcessSupervisor Supervisor { get; init; }

    /// <summary>Conversacion con el modelo. Se poda por los bordes, nunca por el medio de un turno.</summary>
    public List<ChatMessage> Messages { get; } = [];

    public DateTimeOffset StartedAt { get; } = DateTimeOffset.Now;

    /// <summary>Llamadas al modelo. No son turnos de conversacion: son reacciones a eventos.</summary>
    public int Cycles { get; set; }

    public int EventsSeen { get; set; }

    public int ToolCalls { get; set; }

    public int IdleWaits { get; set; }

    /// <summary>
    /// Respuestas seguidas del modelo sin ninguna tool call. Se resetea a 0 en
    /// cuanto el modelo usa una herramienta. El reactor lo usa para endurecer
    /// el aviso: a partir de la segunda narracion seguida se le muestra un
    /// ejemplo concreto de llamada, porque la lista de tarjetas legales sola
    /// no basta para algunos modelos.
    /// </summary>
    public int TextOnlyStreak { get; set; }

    /// <summary>
    /// Tool calls seguidas sin jugar ninguna tarjeta. Se resetea a 0 cuando el
    /// modelo juega una tarjeta. El reactor lo usa para detectar el otro modo
    /// de estancamiento: el modelo ejecuta herramientas (ejecutar, leer, etc.)
    /// pero nunca declara progreso con tarjetas. A partir de 3 seguidas se le
    /// recuerda con ejemplo concreto.
    /// </summary>
    public int ToolCallsSinceCard { get; set; }

    public TokenUsage TotalUsage { get; set; } = TokenUsage.Empty;

    public List<ThinkingTrace> Thinking { get; } = [];

    public ParsedCompletion? Last { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Tope de mensajes vivos; el mas viejo se cae por el borde.</summary>
    public int MaxMessages { get; init; } = 40;

    public int MaxHistoryCharacters { get; init; } = 48000;

    /// <summary>Preferencias y antecedentes de sesion, separados del estado de esta operacion.</summary>
    public string? SessionContext { get; set; }

    private readonly Queue<string> _observations = new();
    public void RememberObservation(string tool, string output, bool failed)
    {
        // A bounded index, not a substitute for full journal evidence or a usable receipt.
        var excerpt = output.Length <= 500 ? output : output[..500] + " [ver journal para resultado completo]";
        using var document = JsonDocument.Parse(output);
        var receipt = document.RootElement.TryGetProperty("receipt_id", out var id) ? id.GetString() : null;
        _observations.Enqueue($"{tool}: {(failed ? "FALLO" : "OK")} receipt_id={receipt ?? "ninguno"} {excerpt}");
        while (_observations.Count > 6) _observations.Dequeue();
    }

    public TimeSpan Elapsed => DateTimeOffset.Now - StartedAt;

    public TimeSpan Remaining => Goal.Budget - Elapsed;

    public bool Expired => Remaining <= TimeSpan.Zero;

    /// <summary>
    /// Lo marca el reactor cuando el watchdog del deadline corta una llamada
    /// al modelo o una espera sobre el bus. Lo usa OnExecuteAsync para
    /// distinguir entre cancelacion del usuario (Ctrl+C) y cancelacion
    /// por presupuesto agotado, y devolver Fallido con motivo de timeout
    /// en el segundo caso.
    /// </summary>
    public bool DeadlineReached { get; set; }

    public bool ThinkingObserved => Thinking.Any(t => t.Observed);

    /// <summary>
    /// Poda por el borde antiguo respetando los turnos: si el corte cae sobre resultados de
    /// herramienta, se avanza hasta pasarlos. Dejar mensajes 'tool' huerfanos (sin el
    /// assistant que pidio esas tool_calls) hace que el gateway rechace la conversacion entera.
    /// </summary>
    public void Trim()
    {
        if (Messages.Count <= MaxMessages && JsonSerializer.Serialize(Messages).Length <= MaxHistoryCharacters)
        {
            return;
        }

        var cut = Math.Max(1, Messages.Count - MaxMessages + 1);
        var lastBrief = Messages.FindLastIndex(m => m.Role == ChatRoles.User);
        while (cut < lastBrief && (Messages[cut].Role != ChatRoles.User
            || JsonSerializer.Serialize(Messages.Take(1).Concat(Messages.Skip(cut))).Length > MaxHistoryCharacters))
        {
            cut++;
        }

        // El indice 0 es el prompt de sistema: nunca se tira, es el contrato de la operacion.
        Messages.RemoveRange(1, Math.Max(0, Math.Min(cut, lastBrief) - 1));
    }

    /// <summary>El parte que se le entrega al modelo en cada ciclo: que paso y donde esta.</summary>
    public string Brief(IReadOnlyList<AgentEvent> events)
    {
        var builder = new StringBuilder();

        builder.AppendLine(events.Count == 0
            ? "NOVEDADES: ninguna (nadie ha hecho nada desde el ciclo anterior)."
            : $"NOVEDADES ({events.Count} evento(s), lo mas reciente al final):");

        foreach (var item in events)
        {
            builder.AppendLine(DataGuard.PromptData("event:" + item.Kind, item.ToPrompt(), 2048));
        }

        builder.AppendLine();
        builder.AppendLine("OBSERVACIONES RECIENTES (indice; resultados completos en journal)");
        foreach (var observation in _observations) builder.AppendLine(DataGuard.PromptData("tool_observation", observation, 800));
        builder.AppendLine("ESTADO DE LAS TARJETAS");
        builder.AppendLine(DataGuard.PromptData("ledger_snapshot", Ledger.DescribeState(), 6000));

        var allowed = Ledger.AllowedNow();
        builder.AppendLine(allowed.Count == 0
            ? "tarjetas legales ahora: ninguna (la operacion ya esta cerrada)"
            : $"tarjetas legales ahora: {string.Join(", ", allowed)}");

        builder.AppendLine();
        builder.AppendLine("PROCESOS");
        if (Supervisor.Total == 0)
        {
            builder.AppendLine("  (ninguno lanzado todavia)");
        }
        else
        {
            foreach (var handle in Supervisor.Handles.TakeLast(6))
            {
                builder.AppendLine(DataGuard.PromptData("process_snapshot", handle.ToString(), 1000));
            }
        }

        var watchers = Supervisor.Watchers;
        if (watchers.Count > 0)
        {
            builder.AppendLine("OBSERVADORES");
            foreach (var watcher in watchers.TakeLast(4))
            {
                builder.AppendLine(DataGuard.PromptData("watcher_snapshot", watcher.ToString(), 1000));
            }
        }

        builder.AppendLine();
        builder.AppendLine($"PRESUPUESTO: {Elapsed.TotalSeconds:0}s gastados de {Goal.Budget.TotalSeconds:0}s; quedan {Math.Max(0, Remaining.TotalSeconds):0}s.");

        return builder.ToString().TrimEnd();
    }

    public string Summary()
    {
        var estado = Ledger.GoalMet ? "cumplido" : Ledger.Aborted ? "abortado" : "sin cerrar";
        return $"{estado} | ciclos {Cycles} | eventos {EventsSeen} | tools {ToolCalls} | tarjetas {Ledger.Plays.Count} ({Ledger.Rejected} rechazadas) | procesos {Supervisor.Total} | {Elapsed.TotalSeconds:0}s | {TotalUsage}";
    }
}
