using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    /// <summary>
    /// Visual Studio arranca el binario con los argumentos fijos del perfil, asi que no
    /// hay donde teclear un comando. Cuando no llegan argumentos y hay una consola de
    /// verdad detras, el CLI se ofrece como prompt en vez de imprimir la ayuda y morir.
    /// Con stdin redirigido (tuberias, CI) se mantiene el comportamiento de siempre.
    /// </summary>
    private static bool ShouldRunInteractive(string[] args)
    {
        if (args.Length >= 1 && args[0].ToLowerInvariant() is "interactive" or "interactivo" or "repl")
        {
            return true;
        }

        return args.Length == 0 && !Console.IsInputRedirected;
    }


    /// <summary>
    /// Entrada de objetivos al reactor. ConversationSession conserva antecedentes y
    /// recuerdos; no determina el ciclo de ejecucion de las operaciones.
    /// </summary>
    private static async Task<int> InteractiveAsync(string[] args, CancellationToken startupToken = default)
    {
        if (HasFlag(args, "--project-contract")) return Fail("Usa project-run --project-contract <json> desde el CLI; interactive no admite un contrato de arranque.");
        var thread = new ConversationSession { SessionStore = new FileConversationSessionStore() };
        var tools = BuiltInTools.DefaultRegistry();
        var maxIterations = IntOption(args, "--max-iterations", 24);
        string model;
        ThinkingPolicy policy;
        ExecutionPolicy sessionPolicy;

        try
        {
            thread.Memory = new FileConversationMemory(Path.Combine(SecretStore.ResolveRepositoryRoot(), ".agentloop"));
            ApplyPromptOptions(thread, args);
            model = ResolveModel(args);
            policy = ResolveThinkingPolicy(args);
            sessionPolicy = new ExecutionPolicy
            {
                Mode = ParsePolicyMode(StringOption(args, "--politica")),
                AllowShell = !HasFlag(args, "--no-shell"),
                AllowedPaths = StringOption(args, "--scope-root") is { } scopeRoot ? [Path.GetFullPath(scopeRoot)] : [],
                DefaultShell = ShellCommand.ParseShell(StringOption(args, "--shell")),
                WorkingDirectory = Path.GetFullPath(StringOption(args, "--cwd") ?? Environment.CurrentDirectory),
                MaxConcurrent = IntOption(args, "--max-procesos", 4),
            };
            thread.Variables.WithDefaults(model, tools.Names);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            ConsoleUi.Error($"ERROR: {ex.Message}");
            return 64;
        }

        var settings = new SessionSettings
        {
            Thinking = policy,
            Intent = ParseIntent(StringOption(args,"--intent")),
            Model = model,
            Stream = ResolveStreaming(args, fallback: true),
            Policy = sessionPolicy,
        };

        thread.SetIntent(settings.Intent);
        PrintInteractiveBanner(tools, settings);
        var credentialState = SecretStore.ResolveCredential();
        if (!credentialState.IsOk) ReportFailure(credentialState.Error!);
        else ConsoleUi.Text("Credencial: formato local valido; aceptacion remota pendiente de comprobacion.", ConsoleColor.DarkGray);
        if (credentialState.IsOk && !HasFlag(args, "--offline")) await VerifyCredentialsAsync(startupToken);


        // Un solo cliente para toda la sesion: reusar el HttpClient evita agotar
        // sockets y se crea perezosamente para que 'ayuda' funcione sin key.
        NvidiaClient? client = null;
        var lastExitCode = 0;
        Task<int>? activeOperation = null;
        EventOperationCoordinator? activeCoordinator = null;
        settings.OnCoordinator = coordinator => Interlocked.Exchange(ref activeCoordinator, coordinator);

        async Task<int> RunOperationAsync(string prompt)
        {
            var code = await GuardedAsync(async cancellationToken =>
            {
                if (client is null) client = BuildClient(out _, policy);
                return await AgentOperationAsync(client, thread, prompt, settings,
                    IntOption(args, "--max-ciclos", 80), IntOption(args, "--deadline", 300), cancellationToken);
            });
            lastExitCode = code;
            ConsoleUi.Status($"Operacion terminada | codigo {code}", code == 0);
            return code;
        }

        try
        {
            while (true)
            {
                ConsoleUi.Prompt(thread.Exchanges);
                var line = Console.ReadLine();

                if (line is null)
                {
                    // stdin cerrado: no queda nada que leer, salir es lo unico sensato.
                    Console.WriteLine();
                    return activeOperation is null ? lastExitCode : await activeOperation;
                }

                line = line.Trim();
                if (line.Length == 0)
                {
                    continue;
                }

                var tokens = SplitArguments(line);
                if (tokens.Length == 0)
                {
                    continue;
                }

                var verb = tokens[0].ToLowerInvariant();
                var rest = string.Join(' ', tokens.Skip(1));

                if (verb == "permisos")
                {
                    if (rest.Equals("revocar", StringComparison.OrdinalIgnoreCase))
                    {
                        var coordinator = Volatile.Read(ref activeCoordinator);
                        if (coordinator is null) settings.Policy.RevokeContinuousPermissions();
                        else coordinator.RevokeContinuousPermissions();
                        ConsoleUi.Text("Revocacion de permisos continuos solicitada.", ConsoleColor.Yellow);
                    }
                    else
                    {
                        ConsoleUi.Section("PERMISOS  /  Solo esta sesion");
                        foreach (var permission in settings.Policy.ContinuousPermissions) ConsoleUi.Text(permission);
                        if (settings.Policy.ContinuousPermissions.Count == 0) ConsoleUi.Text("Sin permisos continuos.");
                        ConsoleUi.Text("permisos revocar: elimina todos los permisos continuos.");
                    }
                    continue;
                }

                if (verb is "salir" or "exit" or "quit" or "q")
                {
                    Volatile.Read(ref activeCoordinator)?.Control(AgentEventKind.Cancelacion);
                    return activeOperation is null ? lastExitCode : await activeOperation;
                }

                if (verb is "pausa" or "reanudar" or "estado" or "cancelar" or "aprobar" or "rechazar")
                {
                    var coordinator = Volatile.Read(ref activeCoordinator);
                    if (coordinator is null) ConsoleUi.Text("No hay operacion activa.", ConsoleColor.DarkGray);
                    else if (verb is "aprobar" or "rechazar")
                    {
                        var continuous = tokens.Skip(1).Any(t => t.Equals("continuo", StringComparison.OrdinalIgnoreCase));
                        var id = tokens.Skip(1).FirstOrDefault(t => !t.Equals("continuo", StringComparison.OrdinalIgnoreCase));
                        if (!coordinator.Approve(verb == "aprobar", continuous, id))
                            ConsoleUi.Text("Sin solicitud unica pendiente. Usa aprobar/rechazar <approval_id>.", ConsoleColor.Yellow);
                    }
                    else coordinator.Control(verb switch
                    {
                        "pausa" => AgentEventKind.Pausa, "reanudar" => AgentEventKind.Reanudacion,
                        "estado" => AgentEventKind.ConsultaEstado, _ => AgentEventKind.Cancelacion,
                    });
                    continue;
                }
                if (activeOperation is { IsCompleted: false })
                {
                    ConsoleUi.Text("Operacion activa. Usa estado, pausa, reanudar, cancelar o aprobar/rechazar.", ConsoleColor.Yellow);
                    continue;
                }

                if (verb is "skills" or "code-review" or "repo-map" or "ediciones" or "credenciales")
                {
                    lastExitCode = await ExecuteAsync(tokens);
                    continue;
                }
                if (verb == "setup")
                {
                    lastExitCode = await ExecuteAsync(tokens);
                    client?.Dispose();
                    client = null;
                    continue;
                }

                if (TryHandleSessionCommand(thread, settings, verb, rest))
                {
                    continue;
                }

                Console.WriteLine();

                // Las utilidades sueltas no tocan el hilo; el agente si.
                if (verb is "project-check-backend" or "selftest-projects" or "project-run" or "project-inspect" or "project-reconcile" or "project-admit" or "discover" or "selftest" or "selftest-events" or "selftest-coordinator" or "recuperacion" or "goal" or "objetivo" or "journal" or "diario" or "lobos" or "diagnostico" or "help" or "--help" or "-h" or "ayuda")
                {
                    lastExitCode = await ExecuteAsync(tokens);
                }
                else
                {
                    var prompt = verb is "run" or "chat" or "run-chat" ? rest : line;
                    if (prompt.Length == 0)
                    {
                        Console.WriteLine("Escribe el texto despues de 'run'.");
                        continue;
                    }

                    if (!UsesConversationLoop(verb))
                    {
                        activeOperation = RunOperationAsync(prompt);
                        continue;
                    }

                    lastExitCode = await GuardedAsync(async cancellationToken =>
                    {
                        if (client is null)
                        {
                            // Un solo cliente y una sola politica para la sesion: lo que aprenda
                            // sobre el thinking de un modelo vale para los turnos siguientes.
                            client = BuildClient(out var keySource, policy);
                            ConsoleUi.Field("Credencial", keySource);
                            Console.WriteLine();
                        }

                        return UsesConversationLoop(verb)
                            ? await AgentTurnAsync(client, thread, prompt, tools, settings, maxIterations, cancellationToken)
                            : await AgentOperationAsync(client, thread, prompt, settings,
                                IntOption(args, "--max-ciclos", 80), IntOption(args, "--deadline", 300), cancellationToken);
                    });
                }

                Console.WriteLine();
                ConsoleUi.Status($"Codigo de salida: {lastExitCode}", lastExitCode == 0);
                Console.WriteLine();
            }
        }
        finally
        {
            client?.Dispose();
        }
    }


    /// <summary>
    /// Lo que la sesion interactiva puede cambiar en caliente. Va por referencia para que
    /// 'pensar' y 'stream' afecten al turno siguiente sin rearrancar el CLI.
    /// </summary>
    internal sealed class SessionSettings
    {
        public required ThinkingPolicy Thinking { get; init; }

        public required string Model { get; set; }

        public IReadOnlyList<ArtifactCriterion>? Criteria { get; set; }
        public AgentLoop.Operations.OperationIntent Intent { get; set; }
        public bool Stream { get; set; }
        public ExecutionPolicy Policy { get; init; } = new();
        public Action<EventOperationCoordinator?>? OnCoordinator { get; set; }
    }


    /// <summary>Comandos que solo existen dentro de la sesion. Devuelve true si consumio la linea.</summary>
    private static bool TryHandleSessionCommand(
        ConversationSession thread,
        SessionSettings settings,
        string verb,
        string rest)
    {
        var policy = settings.Thinking;
        var model = settings.Model;

        if (verb == "intencion") { RunSessionEdit(() => { settings.Intent = ParseIntent(rest); thread.SetIntent(settings.Intent); ConsoleUi.Field("Intencion",settings.Intent); }); return true; }
        if (verb is "objetivo-fijar" or "restriccion")
        {
            RunSessionEdit(() =>
            {
                if (verb=="objetivo-fijar") thread.BindObjective(rest=="limpiar" ? "" : rest);
                else if (rest=="limpiar") thread.ClearConstraints(); else thread.AddConstraint(rest);
                ConsoleUi.Field("Tarea",thread.TaskState.Objective); ConsoleUi.Field("Restricciones",thread.TaskState.Constraints.Count);
            }); return true;
        }
        if (verb == "criterio")
        {
            RunSessionEdit(() =>
            {
                if (rest == "limpiar") settings.Criteria = null;
                else
                {
                    var path=Path.GetFullPath(rest.Trim().Trim('"'),settings.Policy.WorkingDirectory);
                    ProjectContractAdmission.CheckResource(path);
                    if (!Scope.Minimal(settings.Policy).AllowsPath(path)) throw new InvalidOperationException("Criterio fuera del scope.");
                    settings.Criteria=(settings.Criteria ?? []).Append(new ArtifactCriterion(path)).ToArray();
                }
                ConsoleUi.Field("Criterios",settings.Criteria?.Count ?? 0);
            }); return true;
        }
        switch (verb)
        {
            case "memoria":
                ConsoleUi.Section("MEMORIA  /  Recuerdos persistidos");                if (thread.Memory is null || thread.Memory.Notes.Count == 0) ConsoleUi.Text("Sin recuerdos guardados.", ConsoleColor.DarkGray);
                else foreach (var note in thread.Memory.Notes) ConsoleUi.Field(note.Key, note.Value);
                return true;

            case "recordar":
                RunSessionEdit(() =>
                {
                    if (thread.Memory is null) throw new InvalidOperationException("Memoria no inicializada.");
                    thread.Memory.Remember(rest);
                    ConsoleUi.Text("  Recuerdo guardado para las siguientes sesiones.");
                });
                return true;

            case "olvidar":
                RunSessionEdit(() => ConsoleUi.Text(thread.Memory?.Forget(rest) == true ? "  Recuerdo eliminado." : "  No existe esa clave."));
                return true;

            case "contexto":
                RunSessionEdit(() =>
                {
                    if (rest.Equals("compactar", StringComparison.OrdinalIgnoreCase)) thread.Compact();
                    else if (rest.StartsWith("limite ", StringComparison.OrdinalIgnoreCase) && int.TryParse(rest[7..].Trim(), out var limit)) thread.SetHistoryLimit(limit);
                    else if (rest.Length > 0) throw new InvalidOperationException("Usa contexto, contexto compactar o contexto limite <caracteres>.");
                    ConsoleUi.Section("CONTEXTO  /  Uso y limites");
                    ConsoleUi.Field("Mensajes", $"{thread.Messages.Count} / {thread.MaxMessages}");
                    ConsoleUi.Field("Caracteres", $"{thread.HistoryCharacters} / {thread.MaxHistoryCharacters}");
                    ConsoleUi.Field("Descartados", thread.DroppedMessages);
                    ConsoleUi.Field("Recuerdos", thread.Memory?.Notes.Count ?? 0);
                    ConsoleUi.Text("Compactar conserva el ultimo intercambio; nuevo limpia el historial.", ConsoleColor.DarkGray);
                });
                return true;

            case "sesion":
                RunSessionEdit(() =>
                {
                    var path = Path.Combine(SecretStore.ResolveRepositoryRoot(), ".agentloop", "session.json");
                    if (rest.Equals("guardar", StringComparison.OrdinalIgnoreCase)) thread.SaveSession(path);
                    else if (rest.Equals("cargar", StringComparison.OrdinalIgnoreCase)) thread.LoadSession(path);
                    else throw new InvalidOperationException("Usa sesion guardar o sesion cargar.");
                    ConsoleUi.Text($"  Sesion {rest}: {path} ({thread.Messages.Count} mensajes).");
                });
                return true;

            case "modelos" or "models":
                ConsoleUi.Section("MODELOS  /  Catalogo configurado");
                ConsoleUi.Text("La lista configurada no acredita disponibilidad remota.", ConsoleColor.DarkGray);
                foreach (var available in NvidiaOptions.SupportedModels)
                {
                    ConsoleUi.Text($"{(string.Equals(available, settings.Model, StringComparison.OrdinalIgnoreCase) ? "[activo]" : "        ")} {available}", string.Equals(available, settings.Model, StringComparison.OrdinalIgnoreCase) ? ConsoleColor.Cyan : ConsoleColor.Gray, 2);
                }
                return true;

            case "modelo" or "model":
                RunSessionEdit(() =>
                {
                    var requested = rest.Trim();
                    if (requested.Length == 0)
                    {
                        ConsoleUi.Text($"  modelo actual: {settings.Model}");
                        return;
                    }

                    var selected = NvidiaOptions.SupportedModels.FirstOrDefault(m =>
                        string.Equals(m, requested, StringComparison.OrdinalIgnoreCase)
                        || (requested.Equals("glm", StringComparison.OrdinalIgnoreCase) && m == NvidiaOptions.FallbackModelId)
                        || (requested.Equals("kimi", StringComparison.OrdinalIgnoreCase) && m == NvidiaOptions.KimiK3ModelId));

                    if (selected is null)
                    {
                        throw new InvalidOperationException($"modelo no reconocido: '{requested}'. Usa 'modelos'.");
                    }

                    settings.Model = selected;
                    thread.Variables.Set("modelo", selected);
                    ConsoleUi.Text($"  modelo cambiado a: {selected}");
                });
                return true;

            case "stream" or "vivo":
                RunSessionEdit(() =>
                {
                    var value = rest.Trim();
                    if (value.Length > 0)
                    {
                        settings.Stream = value.ToLowerInvariant() switch
                        {
                            "on" or "si" or "true" or "1" => true,
                            "off" or "no" or "false" or "0" => false,
                            _ => throw new InvalidOperationException($"stream: valor no reconocido '{value}' (usa on u off)."),
                        };
                    }

                    ConsoleUi.Text($"  stream: {(settings.Stream ? "activo (se pinta segun llega)" : "apagado (respuesta completa)")}");
                });
                return true;

            case "pensar" or "thinking":
                RunSessionEdit(() =>
                {
                    var value = rest.Trim();
                    if (value.Length > 0)
                    {
                        // El presupuesto se acepta pegado: 'pensar on 512'.
                        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        policy.Preference = ThinkingPolicy.ParsePreference(parts[0]);
                        if (parts.Length > 1 && int.TryParse(parts[1], out var budget))
                        {
                            policy.BudgetTokens = budget;
                        }
                    }

                    ConsoleUi.Text($"  thinking: {DescribeThinking(policy, model)}");
                });
                return true;

            case "nuevo" or "reset":
                thread.Clear();
                ConsoleUi.Text("Hilo reiniciado; las variables y la plantilla se mantienen.");
                return true;

            case "historial":
                PrintThread(thread);
                return true;

            case "vars":
                PrintVariables(thread);
                return true;

            case "sistema":
                PrintSystemPrompt(thread);
                return true;

            case "set":
                RunSessionEdit(() =>
                {
                    thread.Variables.SetAssignment(rest);
                    ConsoleUi.Text($"  {rest[..rest.IndexOf('=')].Trim()} = {thread.Variables[rest[..rest.IndexOf('=')].Trim()]}");
                });
                return true;

            case "unset":
                ConsoleUi.Text(thread.Variables.Remove(rest.Trim())
                    ? $"  {rest.Trim()} eliminada."
                    : $"  {rest.Trim()} no estaba definida.");
                return true;

            case "plantilla":
                RunSessionEdit(() =>
                {
                    var template = File.Exists(rest)
                        ? PromptTemplate.Parse(File.ReadAllText(rest))
                        : PromptTemplate.Parse(rest);

                    thread.UseSystemTemplate(template);
                    ConsoleUi.Text($"  plantilla cargada ({template.Placeholders.Count} variables).");
                    PrintSystemPrompt(thread);
                });
                return true;

            default:
                return false;
        }
    }


    /// <summary>Los errores de plantilla no deben tumbar la sesion, solo el comando.</summary>
    private static void RunSessionEdit(Action edit)
    {
        try
        {
            edit();
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or JsonException or UnauthorizedAccessException)
        {
            ConsoleUi.Error($"  ERROR: {ex.Message}");
        }
    }


    private static void PrintInteractiveBanner(ToolRegistry tools, SessionSettings settings)
    {
        ConsoleUi.Section("AGENTLOOP  /  Consola de operaciones");
        ConsoleUi.Field("Modelo", settings.Model);
        ConsoleUi.Field("Proveedor", "NVIDIA NIM");
        ConsoleUi.Field("Modelos", string.Join(", ", NvidiaOptions.SupportedModels));
        ConsoleUi.Field("Razonamiento", DescribeThinking(settings.Thinking, settings.Model));
        ConsoleUi.Field("Stream", settings.Stream ? "activo" : "apagado");
        ConsoleUi.Field("Directorio", settings.Policy.WorkingDirectory);
        ConsoleUi.Field("Tools base", string.Join(", ", tools.Names));
        ConsoleUi.Field("Terminal", settings.Policy.AllowShell && settings.Policy.AllowedPaths.Count == 0
            ? "run/chat: ejecutar para comandos cortos; lanzar para procesos largos"
            : "Shell deshabilitado por politica o scope de rutas");
        ConsoleUi.Text("Cada operacion muestra su catalogo efectivo de herramientas.", ConsoleColor.DarkGray);
        ConsoleUi.Text("Motor por eventos y tarjetas. El cierre exige evidencia comprobada.", ConsoleColor.DarkGray);
        ConsoleUi.Section("COMANDOS  /  Objetivos, contexto y control");
        ConsoleUi.Text("  <texto libre>        publica un objetivo al reactor con memoria y antecedentes");
        ConsoleUi.Text("  run <texto>          igual, explicito; guarda evidencia local de la operacion");
        ConsoleUi.Text("  modelos              lista glm-5.3 y kimi-k3");
        ConsoleUi.Text("  modelo <glm|kimi|id> cambia el modelo para la siguiente operacion");
        ConsoleUi.Text("  nuevo                olvida la conversacion (mantiene variables y plantilla)");
        ConsoleUi.Text("  historial            muestra los mensajes vivos del hilo");
        ConsoleUi.Text("  contexto [compactar|limite N]   consulta o limita el historial en caracteres");
        ConsoleUi.Text("  recordar clave=valor | memoria | olvidar clave   recuerdos por proyecto");
        ConsoleUi.Text("  sesion guardar|cargar   guarda o recupera el historial local");
        ConsoleUi.Text("  vars                 lista los parametros del prompt");
        ConsoleUi.Text("  set clave=valor      define un parametro (p.ej. set idioma=ingles)");
        ConsoleUi.Text("  unset clave          lo quita");
        ConsoleUi.Text("  sistema              muestra el prompt de sistema ya interpolado");
        ConsoleUi.Text("  plantilla <texto|archivo>   cambia la plantilla de sistema");
        ConsoleUi.Text("  pensar on|off|auto [budget] activa o apaga el razonamiento del modelo");
        ConsoleUi.Text("  stream on|off        pinta la respuesta segun llega o de golpe");
        ConsoleUi.Text("  estado | pausa | reanudar | cancelar   controles de la operacion activa");
        ConsoleUi.Text("  aprobar [id] [continuo] | rechazar [id]   autorizacion del operador");
        ConsoleUi.Text("  permisos | permisos revocar   consulta o revoca permisos de sesion");
        ConsoleUi.Text("  chat <texto>          alias de run: reactor, terminal y evidencia local");
        ConsoleUi.Text("  run-chat <texto>      utilidad conversacional anterior, sin terminal");
        ConsoleUi.Text("  objetivo-fijar <texto>|limpiar | restriccion <texto>|limpiar   contexto explicito");
        ConsoleUi.Text("  criterio <ruta>|limpiar   artefactos que deben cambiar; requerido para Edit/Build");
        ConsoleUi.Text("  skills [intencion] | code-review --root <ruta> | ediciones   contexto y recuperacion");
        ConsoleUi.Text("  intencion General|Explain|Review|Edit|Build|Diagnose   declara el proposito");
        ConsoleUi.Text("  credenciales verificar   comprueba aceptacion remota con limite de tiempo");
        ConsoleUi.Text("  setup [--status]     configura credenciales con entrada oculta");
        ConsoleUi.Text("  discover | selftest | ayuda   utilidades sueltas");
        ConsoleUi.Text("  salir                cierra");
        ConsoleUi.Text();
    }


    private static void PrintThread(ConversationSession thread)
    {
        ConsoleUi.Section("HISTORIAL  /  Mensajes de la sesion");
        if (thread.Messages.Count == 0)
        {
            Console.WriteLine("  (hilo vacio)");
            return;
        }

        foreach (var message in thread.Messages)
        {
            // Un assistant que solo pide herramientas llega con content vacio, no nulo.
            var body = string.IsNullOrWhiteSpace(message.Content)
                ? (message.ToolCalls is not null ? "(tool_calls)" : "(vacio)")
                : message.Content;
            ConsoleUi.Field(message.Role, Shorten(body.Replace('\n', ' '), 90));
        }

        ConsoleUi.Text($"{thread.Messages.Count} mensajes | limite {thread.MaxMessages}", ConsoleColor.DarkGray);
    }


    private static void PrintVariables(ConversationSession thread)
    {
        ConsoleUi.Section("VARIABLES  /  Parametros de la plantilla");
        foreach (var name in thread.Variables.Names)
        {
            ConsoleUi.Field(name, Shorten(thread.Variables[name].Replace('\n', ' '), 80));
        }

        var missing = thread.SystemTemplate.MissingVariables(thread.Variables);
        if (missing.Count > 0)
        {
            Console.WriteLine($"  FALTAN: {string.Join(", ", missing)}");
        }
    }


    private static void PrintSystemPrompt(ConversationSession thread)
    {
        try
        {
            ConsoleUi.Section("SISTEMA  /  Prompt interpolado");
            foreach (var line in thread.RenderSystemPrompt().Split('\n'))
            {
                ConsoleUi.Text(line, indent: 2);
            }
        }
        catch (PromptTemplateException ex)
        {
            ConsoleUi.Error($"  ERROR: {ex.Message}");
        }
    }


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

}
