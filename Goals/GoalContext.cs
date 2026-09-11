using System.Text;
using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Parsing;
using AgentLoop.Prompting;
using AgentLoop.Provider;
using AgentLoop.Terminals;

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

    public TokenUsage TotalUsage { get; set; } = TokenUsage.Empty;

    public List<ThinkingTrace> Thinking { get; } = [];

    public ParsedCompletion? Last { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Tope de mensajes vivos; el mas viejo se cae por el borde.</summary>
    public int MaxMessages { get; init; } = 40;

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
        if (Messages.Count <= MaxMessages)
        {
            return;
        }

        var cut = Messages.Count - MaxMessages;
        while (cut < Messages.Count && Messages[cut].Role == ChatRoles.Tool)
        {
            cut++;
        }

        // El indice 0 es el prompt de sistema: nunca se tira, es el contrato de la operacion.
        Messages.RemoveRange(1, Math.Max(0, cut - 1));
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
            builder.AppendLine(TextSanitizer.Plain(item.ToPrompt()));
        }

        builder.AppendLine();
        builder.AppendLine("ESTADO DE LAS TARJETAS");
        builder.AppendLine(Ledger.DescribeState());

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
                builder.AppendLine($"  {TextSanitizer.Plain(handle.ToString())}");
            }
        }

        var watchers = Supervisor.Watchers;
        if (watchers.Count > 0)
        {
            builder.AppendLine("OBSERVADORES");
            foreach (var watcher in watchers.TakeLast(4))
            {
                builder.AppendLine($"  {TextSanitizer.Plain(watcher.ToString())}");
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
