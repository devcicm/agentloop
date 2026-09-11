using System.Text;
using System.Text.Json;
using AgentLoop.Events;
using AgentLoop.Parsing;
using AgentLoop.Prompting;
using AgentLoop.Provider;
using AgentLoop.StateMachines;
using AgentLoop.Terminals;
using AgentLoop.Tools;

namespace AgentLoop.Goals;

/// <summary>Estados del reactor. No son turnos: son fases de una reaccion a lo que pasa.</summary>
public enum ReactorState
{
    Arranque,
    Esperando,
    Razonando,
    Despachando,
    Cumplido,
    Fallido,
}

/// <summary>
/// El agente dirigido por eventos.
///
/// La diferencia con <see cref="AgentStateMachine"/> no es cosmetica: alli el ciclo lo marca
/// la conversacion (pregunta, respuesta, herramienta, respuesta) y termina cuando el modelo
/// deja de pedir tools. Aqui el ciclo lo marca lo que ocurre fuera - un proceso que escupe
/// una linea, un patron que aparece, un comando que muere - y lo unico que cierra la
/// operacion es la tarjeta de cierre del libro mayor. Se puede razonar diez veces sin que
/// pase nada, o pasar cien cosas entre dos razonamientos; ninguna de las dos rompe nada.
///
/// El estado <see cref="ReactorState.Esperando"/> no bloquea ningun hilo: espera sobre el
/// reset event del bus, asi que mientras tanto los procesos hijos siguen volcando salida
/// desde sus propios hilos de captura.
/// </summary>
public sealed class EventDrivenAgent : StateMachine<ReactorState, GoalContext>
{
    private static readonly Dictionary<ReactorState, ReactorState[]> Allowed = new()
    {
        [ReactorState.Arranque] = [ReactorState.Razonando, ReactorState.Fallido],
        [ReactorState.Esperando] = [ReactorState.Esperando, ReactorState.Razonando, ReactorState.Cumplido, ReactorState.Fallido],
        [ReactorState.Razonando] = [ReactorState.Despachando, ReactorState.Esperando, ReactorState.Cumplido, ReactorState.Fallido],
        [ReactorState.Despachando] = [ReactorState.Razonando, ReactorState.Esperando, ReactorState.Cumplido, ReactorState.Fallido],
        [ReactorState.Cumplido] = [],
        [ReactorState.Fallido] = [],
    };

    private readonly ITokenRouterClient _client;
    private readonly ToolRegistry _tools;
    private int _transitorios;
    private TimeSpan _pausa;

    public EventDrivenAgent(ITokenRouterClient client, string model, ToolRegistry tools)
        : base(ReactorState.Arranque)
    {
        _client = client;
        _tools = tools;
        Model = model;
    }

    public string Model { get; }

    public int MaxTokens { get; init; } = 900;

    public double Temperature { get; init; } = 0.0;

    public bool ParallelToolCalls { get; init; }

    public ThinkingPolicy? Thinking { get; init; }

    public bool Stream { get; init; }

    public Action<StreamDelta>? OnDelta { get; init; }

    public Action<string>? Trace { get; init; }

    /// <summary>Cuanto se espera de una tacada antes de volver a mirar el reloj y el estado.</summary>
    public TimeSpan IdleSlice { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Eventos que entran como mucho en un solo parte.</summary>
    public int EventsPerBrief { get; init; } = 40;

    /// <summary>Fallos transitorios seguidos del proveedor que se toleran antes de rendirse.</summary>
    public int MaxTransientFailures { get; init; } = 3;

    protected override ReactorState FailureState => ReactorState.Fallido;

    protected override bool IsTerminal(ReactorState state)
        => state is ReactorState.Cumplido or ReactorState.Fallido;

    protected override bool CanTransition(ReactorState from, ReactorState to)
        => Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    protected override ValueTask<ReactorState> OnExecuteAsync(GoalContext context, CancellationToken cancellationToken)
    {
        // El presupuesto es una red, no el criterio: si salta, la operacion se declara
        // fallida precisamente porque nadie llego a jugar la tarjeta de cierre.
        //
        // Despachando queda fuera del corte a proposito: las llamadas ya estan decididas y
        // cada comando tiene su propio tope por politica. Tirarlas aqui solo consigue que
        // la tarjeta de cierre que el modelo acababa de pedir no llegue a jugarse nunca.
        if (context.Expired && CurrentState is ReactorState.Esperando or ReactorState.Razonando)
        {
            context.FailureReason = $"se agoto el presupuesto de {context.Goal.Budget.TotalSeconds:0}s sin tarjeta de cierre";
            PendingNote = context.FailureReason;
            return ValueTask.FromResult(ReactorState.Fallido);
        }

        return CurrentState switch
        {
            ReactorState.Arranque => ValueTask.FromResult(Start(context)),
            ReactorState.Esperando => WaitAsync(context, cancellationToken),
            ReactorState.Razonando => ReasonAsync(context, cancellationToken),
            ReactorState.Despachando => DispatchAsync(context, cancellationToken),
            _ => ValueTask.FromResult(CurrentState),
        };
    }

    /// <summary>
    /// Un 503 del gateway no es el final de la operacion. La capa 1 ya reintenta dentro de
    /// una llamada; esto es el escalon de arriba: si el proveedor se cae con procesos vivos
    /// y presupuesto por delante, se espera y se vuelve, en vez de tirar por la borda una
    /// transaccion a medio abrir por un problema que no es del agente.
    /// </summary>
    protected override ValueTask<ReactorState> OnErrorAsync(Exception error, GoalContext context, CancellationToken cancellationToken)
    {
        var reason = error is TokenRouterException provider
            ? $"[{provider.StatusCode}] {provider.ProviderMessage}"
            : $"{error.GetType().Name}: {error.Message}";

        context.FailureReason = reason;
        Trace?.Invoke($"error: {reason}");

        if (IsTransient(error) && ++_transitorios <= MaxTransientFailures && !context.Expired)
        {
            _pausa = TimeSpan.FromSeconds(Math.Min(15, 3 * _transitorios));
            context.Bus.Publish(
                AgentEventKind.Aviso,
                "reactor",
                $"el proveedor fallo ({reason}); intento {_transitorios} de {MaxTransientFailures}, " +
                $"se reintenta en {_pausa.TotalSeconds:0}s. La operacion sigue abierta.");

            PendingNote = $"fallo transitorio, reintento {_transitorios}";
            return ValueTask.FromResult(ReactorState.Esperando);
        }

        context.Bus.Publish(AgentEventKind.Error, "reactor", reason);
        return base.OnErrorAsync(error, context, cancellationToken);
    }

    /// <summary>Caidas del proveedor y limites de ritmo: culpa de la red, no del plan.</summary>
    private static bool IsTransient(Exception error) => error switch
    {
        TokenRouterException provider => (int)provider.StatusCode >= 500 || (int)provider.StatusCode == 429,
        HttpRequestException or TaskCanceledException or TimeoutException => true,
        _ => false,
    };

    private ReactorState Start(GoalContext context)
    {
        context.Messages.Add(ChatMessage.FromSystem(BuildSystemPrompt(context)));
        context.Bus.Publish(AgentEventKind.Objetivo, "operador", context.Goal.Text);
        Trace?.Invoke($"objetivo: {context.Goal.Text} (presupuesto {context.Goal.Budget.TotalSeconds:0}s)");
        return ReactorState.Razonando;
    }

    /// <summary>
    /// Aqui es donde el agente no bloquea. Se duerme sobre el reset event del bus y lo
    /// despierta cualquiera que publique: una linea de salida, el fin de un proceso, un
    /// patron que aparecio. Si no se mueve nada y tampoco hay nada corriendo, esperar mas
    /// no arregla nada, asi que se le devuelve el control al modelo.
    /// </summary>
    private async ValueTask<ReactorState> WaitAsync(GoalContext context, CancellationToken cancellationToken)
    {
        if (context.Ledger.GoalMet)
        {
            return ReactorState.Cumplido;
        }

        if (_pausa > TimeSpan.Zero)
        {
            // Enfriamiento tras un fallo del proveedor: aqui se espera aunque haya eventos,
            // porque volver corriendo al modelo es justo lo que no hay que hacer.
            var espera = _pausa < context.Remaining ? _pausa : context.Remaining;
            _pausa = TimeSpan.Zero;
            if (espera > TimeSpan.Zero)
            {
                await Task.Delay(espera, cancellationToken).ConfigureAwait(false);
            }

            return ReactorState.Razonando;
        }

        if (context.Bus.PendingCount > 0)
        {
            return ReactorState.Razonando;
        }

        var hasLiveProcesses = context.Supervisor.Running > 0;

        // El slice solo se usa cuando NO hay procesos vivos: es el latido que evita que el
        // modelo se olvide de la operacion abierta. Con procesos vivos la espera es
        // indefinida hasta que el mundo publique (arranque, fin, linea, watcher) o hasta
        // que el plazo del objetivo la corte. Asi no se gasta una llamada al modelo cada
        // 2 s solo para preguntar "¿pasa algo?": es el mismo patron del lector de huellas,
        // donde el wait bloquea hasta que el SDK habla.
        var timeout = hasLiveProcesses
            ? context.Remaining
            : (context.Remaining < IdleSlice ? context.Remaining : IdleSlice);

        var woken = await context.Bus.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (woken)
        {
            PendingNote = "despierta: hay novedades";
            return ReactorState.Razonando;
        }

        context.IdleWaits++;

        if (hasLiveProcesses)
        {
            // El plazo vencio con procesos vivos: no es un latido, es el final de la
            // operacion. El chequeo de context.Expired arriba en OnExecuteAsync ya lo
            // convertira en Fallido si seguimos en Esperando o Razonando.
            PendingNote = "plazo agotado con procesos vivos";
            return ReactorState.Razonando;
        }

        context.Bus.Publish(
            AgentEventKind.Latido,
            "reactor",
            "nada en marcha y nada nuevo: si la operacion sigue abierta, te toca a ti mover.");

        return ReactorState.Razonando;
    }

    private async ValueTask<ReactorState> ReasonAsync(GoalContext context, CancellationToken cancellationToken)
    {
        var events = context.Bus.Drain(EventsPerBrief);
        context.EventsSeen += events.Count;
        context.Messages.Add(ChatMessage.FromUser(context.Brief(events)));
        context.Trim();

        var definitions = _tools.ToDefinitions();
        var thinking = Thinking?.For(Model);

        var request = new ChatRequest
        {
            Model = Model,
            Messages = context.Messages.ToList(),
            Tools = definitions,
            ToolChoice = definitions is null ? null : "auto",
            ParallelToolCalls = definitions is null ? null : ParallelToolCalls,
            Temperature = Temperature,
            MaxTokens = Thinking?.EnsureHeadroom(MaxTokens, Model) ?? MaxTokens,
            Thinking = thinking,
            StreamOptions = Stream ? StreamOptions.WithUsage : null,
        };

        context.Cycles++;
        Trace?.Invoke($"ciclo {context.Cycles}: {events.Count} evento(s) -> modelo ({context.Messages.Count} mensajes)");

        var raw = Stream
            ? await StreamRequestAsync(request, cancellationToken).ConfigureAwait(false)
            : await _client.CreateChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);

        _transitorios = 0; // la llamada salio bien: la racha de fallos se acabo
        context.FailureReason = null;
        var parsed = CompletionParser.Parse(raw);
        context.Last = parsed;
        context.TotalUsage = context.TotalUsage.Add(parsed.Usage);
        context.Thinking.Add(parsed.Thinking);

        if (parsed.Thinking.Observed && Thinking?.MarkSupported(Model) == true)
        {
            Trace?.Invoke($"  thinking confirmado en {Model} ({parsed.Thinking})");
        }

        context.Messages.Add(ChatMessage.FromAssistant(
            TextSanitizer.Plain(parsed.Content),
            parsed.RawToolCalls,
            parsed.Thinking.Text is { Length: > 0 } thought ? TextSanitizer.Plain(thought) : null));

        if (parsed.HasToolCalls)
        {
            PendingNote = $"{parsed.ToolCalls.Count} llamada(s): {string.Join(", ", parsed.ToolCalls.Select(t => t.Name))}";
            return ReactorState.Despachando;
        }

        // Texto suelto sin herramientas: el modelo esta narrando en vez de operar. No se
        // le corta el paso, se le devuelve el aviso como un evento mas y que reaccione.
        var said = parsed.Content is { Length: > 0 } text ? Shorten(text) : "(nada)";
        context.Bus.Publish(
            AgentEventKind.Aviso,
            "reactor",
            $"respondiste texto sin usar herramientas ({said}). La operacion no se cierra hablando: " +
            $"juega una tarjeta o ejecuta algo. Legales ahora: {string.Join(", ", context.Ledger.AllowedNow())}.");

        PendingNote = "respuesta sin herramientas";
        return ReactorState.Esperando;
    }

    private async ValueTask<ReactorState> DispatchAsync(GoalContext context, CancellationToken cancellationToken)
    {
        var completion = context.Last
            ?? throw new InvalidOperationException("No hay llamadas que despachar.");

        foreach (var call in completion.ToolCalls)
        {
            var execution = await _tools.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
            context.ToolCalls++;

            var output = TextSanitizer.EnsureJsonObject(execution.Output);
            context.Messages.Add(ChatMessage.FromToolResult(execution.ToolCallId, execution.ToolName, output));
            Trace?.Invoke($"  {call.Name}({Shorten(TextSanitizer.Plain(call.RawArguments), 90)}) -> {Shorten(TextSanitizer.Plain(execution.Output), 140)}");
        }

        if (context.Ledger.GoalMet)
        {
            PendingNote = context.Ledger.Outcome;
            return ReactorState.Cumplido;
        }

        if (context.Ledger.Aborted)
        {
            context.FailureReason = context.Ledger.Outcome ?? "operacion abortada por el agente";
            PendingNote = context.FailureReason;
            return ReactorState.Fallido;
        }

        // Lo lanzado en este despacho puede haber publicado ya. Si hay algo, se razona
        // enseguida; si no, se espera, que es lo que evita quemar llamadas al modelo.
        return context.Bus.PendingCount > 0 ? ReactorState.Razonando : ReactorState.Esperando;
    }

    private async Task<JsonElement> StreamRequestAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var accumulator = new StreamAccumulator();

        await foreach (var chunk in _client.StreamChatCompletionAsync(request, cancellationToken).ConfigureAwait(false))
        {
            var delta = accumulator.Add(chunk);
            if (!delta.IsEmpty)
            {
                OnDelta?.Invoke(delta);
            }
        }

        return accumulator.BuildRaw();
    }

    /// <summary>
    /// El contrato de la operacion. Lleva el mazo entero renderizado porque el modelo tiene
    /// que poder citar el nombre exacto de cada tarjeta y sus campos obligatorios.
    /// </summary>
    public static string BuildSystemPrompt(GoalContext context)
    {
        var policy = context.Supervisor.Policy;
        var builder = new StringBuilder();

        builder.AppendLine("Eres un agente operador. No estas en una conversacion por turnos: estas dentro de una");
        builder.AppendLine("operacion dirigida por eventos. En cada ciclo recibes un parte con lo que ha pasado desde");
        builder.AppendLine("el ciclo anterior y decides que hacer. Nadie te va a contestar: quien te habla es el sistema.");
        builder.AppendLine();

        builder.AppendLine("OBJETIVO");
        builder.AppendLine(TextSanitizer.Plain(context.Goal.Text));
        builder.AppendLine();

        builder.AppendLine("LA OPERACION ES UNA TRANSACCION");
        builder.AppendLine("Declaras cada punto del recorrido jugando una tarjeta de estado con la herramienta tarjeta.");
        builder.AppendLine("El sistema las valida y lleva el libro mayor. Reglas que no puedes saltarte:");
        builder.AppendLine("  1. Nada empieza sin la tarjeta de apertura.");
        builder.AppendLine("  2. No se cierra sin haber comprobado antes al menos una vez.");
        builder.AppendLine("  3. La operacion SOLO cuenta como cumplida cuando juegas la tarjeta de cierre.");
        builder.AppendLine("     Ni el tiempo, ni un buen resultado, ni decir que ya esta la cierran por ti.");
        builder.AppendLine("  4. Puedes abrir una transaccion dentro de otra para una subtarea, pero cada una que");
        builder.AppendLine("     abras la tienes que cerrar, y en orden inverso.");
        builder.AppendLine("  5. Si es imposible, juega la tarjeta de aborto con su motivo. Eso tambien es un final");
        builder.AppendLine("     honesto; dejarlo abierto no lo es.");
        builder.AppendLine();

        builder.AppendLine("MAZO DE TARJETAS");
        builder.AppendLine(context.Ledger.Deck.Render());
        builder.AppendLine();

        builder.AppendLine("COMO SE TRABAJA");
        builder.AppendLine("  - Lo corto se ejecuta con ejecutar, que espera el resultado.");
        builder.AppendLine("  - Lo largo se arranca con lanzar y se sigue con esperar, leer y observar. No te quedes");
        builder.AppendLine("    parado delante de un proceso: la salida y el fin te llegan como eventos.");
        builder.AppendLine("  - La evidencia de la tarjeta de cierre tiene que ser un dato real que hayas visto:");
        builder.AppendLine("    un codigo de salida, una linea de salida concreta, un contenido leido. No lo inventes.");
        builder.AppendLine("  - Si un comando falla, no repitas lo mismo: lee la salida y cambia de enfoque.");
        builder.AppendLine();

        builder.AppendLine("ENTORNO");
        builder.AppendLine($"  sistema: {(OperatingSystem.IsWindows() ? "Windows" : Environment.OSVersion.Platform.ToString())}");
        builder.AppendLine($"  interprete por defecto: {policy.DefaultShell.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  directorio de trabajo: {TextSanitizer.Plain(policy.WorkingDirectory)}");
        builder.AppendLine($"  politica: {policy.Mode.ToString().ToLowerInvariant()}, hasta {policy.MaxConcurrent} procesos a la vez,");
        builder.AppendLine($"  tope por comando {policy.DefaultTimeout.TotalSeconds:0}s (maximo {policy.MaxTimeout.TotalMinutes:0}m)");
        builder.AppendLine($"  presupuesto total de la operacion: {context.Goal.Budget.TotalSeconds:0}s");
        builder.AppendLine("  hay comandos irreversibles vetados por politica; si te rechazan uno, reformula.");

        return builder.ToString().TrimEnd();
    }

    private static string Shorten(string value, int max = 120)
        => value.Length <= max ? value : value[..max] + "...";
}
