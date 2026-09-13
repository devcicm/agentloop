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
    private readonly AgentLoop.Harness.Patterns.RetryPattern _retryPattern;
    private int _transitorios;
    private TimeSpan _pausa;

    public EventDrivenAgent(ITokenRouterClient client, string model, ToolRegistry tools)
        : base(ReactorState.Arranque)
    {
        _client = client;
        _tools = tools;
        Model = model;
        _retryPattern = new AgentLoop.Harness.Patterns.RetryPattern(
            maxAttempts: 3,
            baseDelay: TimeSpan.FromSeconds(3),
            maxDelay: TimeSpan.FromSeconds(15));
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

    protected override async ValueTask<ReactorState> OnExecuteAsync(GoalContext context, CancellationToken cancellationToken)
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
            return ReactorState.Fallido;
        }

        // Watchdog: el deadline del objetivo se transforma en un CancellationToken que
        // se cancela cuando context.Remaining llega a 0. Asi una llamada al modelo
        // o una espera sobre el bus que se cuelga no bloquea el reactor mas alla
        // del budget: la peticion HTTP se cancela, la Task se completa con
        // OperationCanceledException, y aqui abajo se traduce a Fallido con motivo
        // de timeout en vez de propagarse como cancelacion del usuario.
        //
        // Despachando queda fuera del watchdog a proposito, igual que queda fuera
        // del corte por Expired de arriba: las tool calls ya decididas tienen su
        // propio tope por politica y no se cancelan a mitad.
        if (CurrentState == ReactorState.Despachando)
        {
            return await DispatchAsync(context, cancellationToken).ConfigureAwait(false);
        }

        var remaining = context.Remaining;
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        using var deadlineCts = new CancellationTokenSource(remaining);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineCts.Token);
        var linkedToken = linkedCts.Token;

        try
        {
        return CurrentState switch
        {
            ReactorState.Arranque => Start(context),
            ReactorState.Esperando => await WaitAsync(context, linkedToken).ConfigureAwait(false),
            ReactorState.Razonando => await ReasonAsync(context, linkedToken).ConfigureAwait(false),
            _ => CurrentState,
        };
        }
        catch (OperationCanceledException) when (deadlineCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // El watchdog corto la llamada, no el usuario. Se marca el context para
            // que el resto del harness distinga entre cancelado y timeout, y se
            // devuelve Fallido con el motivo explicito.
            context.DeadlineReached = true;
            context.FailureReason = $"watchdog del deadline: presupuesto de {context.Goal.Budget.TotalSeconds:0}s agotado durante una llamada";
            PendingNote = context.FailureReason;
            return ReactorState.Fallido;
        }
    }

    /// <summary>
    /// Un 503 del gateway no es el final de la operacion. La capa 1 ya reintenta dentro de
    /// una llamada; esto es el escalon de arriba: si el proveedor se cae con procesos vivos
    /// y presupuesto por delante, se espera y se vuelve, en vez de tirar por la borda una
    /// transaccion a medio abrir por un problema que no es del agente.
    ///
    /// El backoff exponencial vive en <see cref="AgentLoop.Harness.Patterns.RetryPattern"/>
    /// desde commit C: este metodo solo consume el patron declarativo en vez de llevar su
    /// propia copia de la receta.
    /// </summary>
    protected override ValueTask<ReactorState> OnErrorAsync(Exception error, GoalContext context, CancellationToken cancellationToken)
    {
        var reason = error is TokenRouterException provider
            ? $"[{provider.StatusCode}] {provider.ProviderMessage}"
            : $"{error.GetType().Name}: {error.Message}";

        context.FailureReason = reason;
        Trace?.Invoke($"error: {reason}");

        var context2 = new AgentLoop.Harness.Patterns.PatternContext(
            Attempt: _transitorios + 1,
            LastError: error,
            Elapsed: context.Elapsed,
            Cancelled: cancellationToken.IsCancellationRequested);

        var retry = _retryPattern.Apply(context2);

        if (IsTransient(error) && retry.Action == AgentLoop.Harness.Patterns.PatternAction.Retry && !context.Expired)
        {
            _transitorios++;
            _pausa = retry.RetryAfter;
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
        // Status 0 = sin respuesta HTTP (timeout de transporte, DNS, conexion).
        // Es transitorio por definicion: el servidor nunca dijo que no.
        TokenRouterException provider => (int)provider.StatusCode >= 500 || (int)provider.StatusCode == 429 || (int)provider.StatusCode == 0,
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
        //
        // Si el plazo ya vencio (Remaining <= 0) no esperamos nada: devolvemos
        // Razonando y el watchdog de arriba en OnExecuteAsync convierte en Fallido.
        // Esperar con timeout <= 0 en el AsyncAutoResetEvent del bus cae en un bucle
        // de iteraciones vacias que no aporta nada.
        if (context.Remaining <= TimeSpan.Zero)
        {
            PendingNote = hasLiveProcesses
                ? "plazo agotado con procesos vivos"
                : "plazo agotado sin procesos vivos";
            return ReactorState.Razonando;
        }

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
            context.TextOnlyStreak = 0;
            PendingNote = $"{parsed.ToolCalls.Count} llamada(s): {string.Join(", ", parsed.ToolCalls.Select(t => t.Name))}";
            return ReactorState.Despachando;
        }

        // Texto suelto sin herramientas: el modelo esta narrando en vez de operar. No se
        // le corta el paso, se le devuelve el aviso como un evento mas y que reaccione.
        // A partir de la segunda narracion seguida se le muestra un ejemplo concreto
        // de llamada: la lista de tarjetas legales sola no basta para algunos modelos
        // y cada ciclo desperdiciado cuesta ~1 min de llamada al modelo.
        context.TextOnlyStreak++;
        var said = parsed.Content is { Length: > 0 } text ? Shorten(text) : "(nada)";
        var aviso = $"respondiste texto sin usar herramientas ({said}). La operacion no se cierra hablando: " +
            $"juega una tarjeta o ejecuta algo. Legales ahora: {string.Join(", ", context.Ledger.AllowedNow())}.";
        if (context.TextOnlyStreak >= 2)
        {
            aviso += $" Ejemplo concreto (copia la estructura, adapta el contenido): {BuildToolExample(context)}";
        }
        context.Bus.Publish(AgentEventKind.Aviso, "reactor", aviso);

        PendingNote = "respuesta sin herramientas";
        return ReactorState.Esperando;
    }

    private async ValueTask<ReactorState> DispatchAsync(GoalContext context, CancellationToken cancellationToken)
    {
        var completion = context.Last
            ?? throw new InvalidOperationException("No hay llamadas que despachar.");

        // Cada tool call se ejecuta en su propio try para que un fallo del
        // dispatcher en una llamada (exception de sanitize, add a Messages,
        // Trace, etc.) no impida despachar el resto. El reactor sigue con la
        // siguiente llamada y publica un Error por la que fallo.
        foreach (var call in completion.ToolCalls)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                // El usuario o el watchdog cortaron: no seguimos despachando.
                Trace?.Invoke($"  despacho cancelado en {call.Name}");
                break;
            }

            ToolExecution execution;
            try
            {
                execution = await _tools.ExecuteAsync(call, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Trace?.Invoke($"  {call.Name} cancelado por watchdog o usuario");
                break;
            }
            catch (Exception ex)
            {
                Trace?.Invoke($"  error ejecutando {call.Name}: {ex.GetType().Name}: {ex.Message}");
                context.Bus.Publish(
                    AgentEventKind.Error,
                    "dispatcher",
                    $"fallo {call.Name}: {ex.GetType().Name}: {ex.Message}");
                continue;
            }

            context.ToolCalls++;

            try
            {
                var output = TextSanitizer.EnsureJsonObject(execution.Output);
                context.Messages.Add(ChatMessage.FromToolResult(execution.ToolCallId, execution.ToolName, output));
                Trace?.Invoke($"  {call.Name}({Shorten(TextSanitizer.Plain(call.RawArguments), 90)}) -> {Shorten(TextSanitizer.Plain(execution.Output), 140)}");
            }
            catch (Exception ex)
            {
                Trace?.Invoke($"  error post-despacho de {call.Name}: {ex.GetType().Name}: {ex.Message}");
                context.Bus.Publish(
                    AgentEventKind.Error,
                    "dispatcher",
                    $"fallo post-despacho de {call.Name}: {ex.GetType().Name}: {ex.Message}");
            }

            if (string.Equals(call.Name, "tarjeta", StringComparison.OrdinalIgnoreCase))
            {
                context.ToolCallsSinceCard = 0;
            }
            else
            {
                context.ToolCallsSinceCard++;
            }
        }

        // El modelo ejecuta herramientas pero no declara progreso con tarjetas:
        // sin este aviso puede quemar todo el presupuesto en ejecutar/leer sin
        // que el libro mayor avance. Se resetea al avisar para no spamear.
        if (context.ToolCallsSinceCard >= 3 && !context.Ledger.GoalMet && !context.Ledger.Aborted)
        {
            context.Bus.Publish(
                AgentEventKind.Aviso,
                "reactor",
                $"llevas {context.ToolCallsSinceCard} llamada(s) seguidas sin jugar tarjeta: ejecutar comandos no mueve el libro mayor, solo las tarjetas lo mueven. " +
                $"Ejemplo concreto (copia la estructura, adapta el contenido): {BuildToolExample(context)}");
            context.ToolCallsSinceCard = 0;
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

    /// <summary>
    /// Ejemplo concreto de llamada a la tool tarjeta para el estado actual del
    /// ledger. Elige la tarjeta mas progresiva entre las legales: cerrar si la
    /// transaccion ya esta verificada, comprobar si ya hay avances registrados,
    /// avanzar como paso natural con transaccion abierta, y la primera legal
    /// en otro caso. Los valores son placeholders: el modelo debe adaptarlos
    /// a lo que realmente observo.
    /// </summary>
    internal static string BuildToolExample(GoalContext context)
    {
        var allowed = context.Ledger.AllowedNow();
        if (allowed.Count == 0)
        {
            return "tarjeta({\"nombre\":\"abrir\",\"objetivo\":\"...\",\"plan\":\"...\"})";
        }

        bool Legal(string name) => allowed.Contains(name, StringComparer.OrdinalIgnoreCase);

        string pick;
        var current = context.Ledger.Current;
        var advances = current is null
            ? 0
            : current.Steps.Count(p => p.Accepted && string.Equals(p.Name, "avanzar", StringComparison.OrdinalIgnoreCase));

        if (current?.Verified == true && Legal("cerrar"))
        {
            pick = "cerrar";
        }
        else if (advances >= 1 && Legal("comprobar"))
        {
            pick = "comprobar";
        }
        else if (Legal("avanzar"))
        {
            pick = "avanzar";
        }
        else
        {
            pick = allowed[0];
        }

        var card = context.Ledger.Deck.Find(pick);
        var fields = card is null
            ? new Dictionary<string, string> { ["paso"] = "..." }
            : card.RequiredFields.ToDictionary(
                f => f,
                f => f switch
                {
                    "objetivo" => "lo que hay que conseguir",
                    "plan" => "como pienso conseguirlo",
                    "paso" => "lo que observe al ejecutar el ultimo comando",
                    "comprobacion" => "que verifique contra salida real",
                    "resultado" => "lo que dice la salida real",
                    "evidencia" => "codigo de salida y linea exacta vista",
                    "motivo" => "por que no es alcanzable",
                    _ => "...",
                },
                StringComparer.Ordinal);

        var args = string.Join(",", fields.Select(kv => $"\"{kv.Key}\":\"{kv.Value}\""));
        return $"tarjeta({{\"nombre\":\"{pick}\",{args}}})";
    }
}
