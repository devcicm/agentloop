using System.Text.Json;
namespace AgentLoop.Goals;


/// <summary>
/// Las herramientas con las que el agente opera: jugar tarjetas de estado y manejar
/// terminales.
///
/// Reparto deliberado: <c>ejecutar</c> es para lo corto (espera el resultado), y
/// <c>lanzar</c> + <c>esperar</c>/<c>leer</c>/<c>observar</c> es para lo largo, que es lo
/// que evita que el agente se quede parado delante de un proceso de diez minutos. Ninguna
/// de las dos bloquea un hilo: la espera es sobre reset events.
/// </summary>
public static class OperationTools
{
    public static ToolRegistry Build(CardLedger ledger, IProcessSupervisor supervisor, EventBus bus)
    {
        // IdempotentTool solo se aplica a tools verdaderamente read-only:
        // procesos (lista snapshot estable) y kill (matar un proceso ya
        // muerto es no-op). leer NO se cachea porque su llamada real
        // avanza el cursor del ProcessHandle, asi dos llamadas con la
        // misma clave devolverian contenido cacheado aunque el proceso
        // haya seguido emitiendo. observar NO se cachea porque cada
        // llamada registra un watcher nuevo con id nuevo. ejecutar,
        // lanzar, esperar, flujo y tarjeta tampoco (efectos reales).
        //
        // El cache lleva TTL de 30s: dentro de un run activo la lista de
        // procesos cambia (arrancan y terminan), asi que un snapshot de
        // hace minutos es peor que volver a preguntar.
        var cache = new AgentLoop.Harness.Concurrency.IdempotencyCache(capacity: 256, ttl: TimeSpan.FromSeconds(30));

        return new ToolRegistry(
        [
            CardTool(ledger, bus),
            RunTool(supervisor),
            LaunchTool(supervisor),
            WaitTool(supervisor, bus),
            ReadTool(supervisor),
            ProcessesTool(supervisor),
            new AgentLoop.Tools.IdempotentTool(KillTool(supervisor), cache),
            WatchTool(supervisor),
            FlowTool(supervisor),
        ]);
    }

    private static DelegateTool CardTool(CardLedger ledger, EventBus bus)
    {
        var deck = ledger.Deck;
        var names = string.Join(", ", deck.Names.Select(n => $"\"{n}\""));

        var schema = $$"""
        {
          "type": "object",
          "properties": {
            "nombre": { "type": "string", "enum": [{{names}}], "description": "Tarjeta de estado que juegas." },
            "objetivo": { "type": "string", "description": "Que se consigue con esta operacion (tarjeta de apertura)." },
            "plan": { "type": "string", "description": "Como piensas conseguirlo (tarjeta de apertura)." },
            "paso": { "type": "string", "description": "El paso que has dado o vas a dar (tarjeta de avance)." },
            "comprobacion": { "type": "string", "description": "Que has verificado (tarjeta de comprobacion)." },
            "resultado": { "type": "string", "description": "Resultado observado (comprobacion y cierre)." },
            "evidencia": { "type": "string", "description": "Dato real que lo respalda: codigo de salida, linea de salida, contenido." },
            "evidencia_id": { "type": "string", "description": "receipt_id devuelto por una herramienta exitosa de esta transaccion. Obligatorio al comprobar en el coordinador por eventos." },
            "motivo": { "type": "string", "description": "Por que se aborta (tarjeta de aborto)." },
            "work_item_id": { "type": "string", "description": "Proyecto: tarea vigente del host." },
            "attempt_id": { "type": "string", "description": "Proyecto: intento vigente del host." }
          },
          "required": ["nombre"]
        }
        """;

        return new DelegateTool(
            name: "tarjeta",
            description: "Juega una tarjeta de estado para declarar en que punto de la operacion estas. " +
                         "El sistema la valida: una tarjeta fuera de orden o sin sus campos se rechaza y te dice por que. " +
                         "La operacion NO cuenta como cumplida hasta que juegues la tarjeta de cierre. " +
                         "El cierre se valida contra evidencia real en codigo: si el objetivo menciona un archivo, " +
                         "debe existir con contenido, y si falta algo el rechazo te dice que (required_evidence).",
            parametersJsonSchema: schema,
            handler: (arguments, _) =>
            {
                var name = ReadString(arguments, "nombre");
                var fields = deck.AllFields.Concat(["evidencia_id", "work_item_id", "attempt_id"])
                    .Select(field => (field, value: ReadString(arguments, field)))
                    .Where(pair => pair.value.Length > 0)
                    .ToDictionary(pair => pair.field, pair => pair.value, StringComparer.Ordinal);

                var play = ledger.Play(name, fields);

                bus.Publish(
                    play.Accepted ? AgentEventKind.Tarjeta : AgentEventKind.TarjetaRechazada,
                    "tarjetas",
                    play.ToString(),
                    new { tarjeta = play.Name, aceptada = play.Accepted, nivel = play.Depth });

                return ValueTask.FromResult(Json(new
                {
                    aceptada = play.Accepted,
                    tarjeta = play.Name,
                    nivel = play.Depth,
                    motivo = play.Rejection,
                    abiertas = ledger.OpenCount,
                    pendiente = ledger.PendingDebt(),
                    legales_ahora = ledger.AllowedNow(),
                    objetivo_cumplido = ledger.GoalMet,
                    evidencia_faltante = ledger.RequiredEvidence(),
                }));
            });
    }

    private static DelegateTool RunTool(IProcessSupervisor supervisor)
        => new(
            name: "ejecutar",
            description: "Ejecuta un comando en una terminal y espera a que termine. Para cosas cortas. " +
                         "Si puede tardar, usa lanzar en vez de esto. Los efectos destructivos requieren aprobacion del operador; no puedes otorgartela.",
            parametersJsonSchema: """
            {
              "type": "object",
              "properties": {
                "comando": { "type": "string", "description": "Script completo con saltos de linea reales. Admite here-strings de PowerShell y heredocs de bash/sh. En JSON usa \n para representar cada salto; al decodificar debe ser un salto real." },
                "shell": { "type": "string", "enum": ["bash", "sh", "powershell", "pwsh", "cmd"], "description": "Interprete. Por defecto el nativo del sistema." },
                "timeout_ms": { "type": "integer", "description": "Tope de espera en milisegundos (60000 por defecto)." },
                "motivo": { "type": "string", "description": "Para que lo ejecutas. Queda en la traza." }
              },
              "required": ["comando"]
            }
            """,
            handler: async (arguments, cancellationToken) =>
            {
                var command = new ShellCommand(
                    ResolveShell(arguments, supervisor),
                    ReadString(arguments, "comando"),
                    Label: ReadString(arguments, "motivo"));

                var timeout = TimeSpan.FromMilliseconds(ReadInt(arguments, "timeout_ms", 60_000));
                var result = await supervisor.RunAsync(command, timeout, cancellationToken).ConfigureAwait(false);

                return Json(new
                {
                    proceso = result.ProcessId,
                    codigo = result.ExitCode,
                    agotado_el_tiempo = result.TimedOut,
                    simulado = result.Simulated,
                    duracion_ms = (int)result.Duration.TotalMilliseconds,
                    truncado = result.Truncated,
                    salida = result.Output.Select(l => l.ToString()).ToArray(),
                });
            });

    private static DelegateTool LaunchTool(IProcessSupervisor supervisor)
        => new(
            name: "lanzar",
            description: "Arranca un comando en segundo plano y vuelve al instante con su identificador. " +
                         "Usalo para servidores, builds o cualquier cosa larga: luego te enteras por eventos, " +
                         "o con esperar, leer y observar.",
            parametersJsonSchema: """
            {
              "type": "object",
              "properties": {
                "comando": { "type": "string", "description": "Script completo con saltos de linea reales. Admite here-strings de PowerShell y heredocs de bash/sh. En JSON usa \n para representar cada salto; al decodificar debe ser un salto real." },
                "shell": { "type": "string", "enum": ["bash", "sh", "powershell", "pwsh", "cmd"], "description": "Interprete. Por defecto el nativo del sistema." },
                "etiqueta": { "type": "string", "description": "Nombre corto para reconocerlo en los eventos." }
              },
              "required": ["comando"]
            }
            """,
            handler: async (arguments, cancellationToken) =>
            {
                var command = new ShellCommand(
                    ResolveShell(arguments, supervisor),
                    ReadString(arguments, "comando"),
                    Label: ReadString(arguments, "etiqueta"));

                var handle = await supervisor.LaunchAsync(command, cancellationToken).ConfigureAwait(false);

                return Json(new
                {
                    proceso = handle.Id,
                    pid = handle.Pid,
                    vivo = handle.Running,
                    nota = "no espera: la salida y el fin te llegaran como eventos.",
                });
            });

    private static DelegateTool WaitTool(IProcessSupervisor supervisor, EventBus bus)
        => new(
            name: "esperar",
            description: "Espera a que pase algo, con tope de tiempo: el fin de un proceso, la aparicion de un " +
                         "patron en la salida, o simplemente el proximo evento. No consume el hilo del agente.",
            parametersJsonSchema: """
            {
              "type": "object",
              "properties": {
                "proceso": { "type": "string", "description": "Identificador devuelto por lanzar. Espera a que termine." },
                "patron": { "type": "string", "description": "Expresion regular a vigilar en la salida que llegue a partir de ahora." },
                "timeout_ms": { "type": "integer", "description": "Tope de espera en milisegundos (15000 por defecto)." }
              }
            }
            """,
            handler: async (arguments, cancellationToken) =>
            {
                var timeout = TimeSpan.FromMilliseconds(ReadInt(arguments, "timeout_ms", 15_000));
                var processId = ReadString(arguments, "proceso");
                var pattern = ReadString(arguments, "patron");

                if (pattern.Length > 0)
                {
                    var watcher = supervisor.Watch(pattern, processId.Length > 0 ? processId : null);
                    bool hit;
                    try { hit = await watcher.Hit.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
                    finally { supervisor.RemoveWatcher(watcher.Id); }

                    return Json(new
                    {
                        evento = hit ? "patron" : "tiempo agotado",
                        observador = watcher.Id,
                        linea = watcher.MatchedLine,
                        proceso = watcher.MatchedProcess,
                    });
                }

                if (processId.Length > 0)
                {
                    var handle = supervisor.Find(processId);
                    if (handle is null)
                    {
                        return Json(new { error = $"no existe el proceso {processId}" });
                    }

                    var ended = await handle.Terminado.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);

                    return Json(new
                    {
                        evento = ended ? "fin" : "sigue vivo",
                        proceso = handle.Id,
                        codigo = handle.ExitCode,
                        sin_leer = handle.Unread,
                        duracion_ms = (int)handle.Elapsed.TotalMilliseconds,
                    });
                }

                var any = await bus.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
                return Json(new
                {
                    evento = any ? "hay novedades" : "silencio",
                    pendientes = bus.PendingCount,
                });
            });

    private static DelegateTool ReadTool(IProcessSupervisor supervisor)
        => new(
            name: "leer",
            description: "Devuelve la salida nueva de un proceso desde la ultima lectura. Cada linea se entrega una sola vez.",
            parametersJsonSchema: """
            {
              "type": "object",
              "properties": {
                "proceso": { "type": "string", "description": "Identificador del proceso." },
                "lineas": { "type": "integer", "description": "Maximo de lineas a devolver (40 por defecto)." },
                "desde_el_final": { "type": "boolean", "description": "true para releer el final aunque ya se hubiera leido." }
              },
              "required": ["proceso"]
            }
            """,
            handler: (arguments, _) =>
            {
                var handle = supervisor.Find(ReadString(arguments, "proceso"));
                if (handle is null)
                {
                    return ValueTask.FromResult(Json(new { error = "no existe ese proceso" }));
                }

                var max = ReadInt(arguments, "lineas", 40);
                var lines = ReadBool(arguments, "desde_el_final") ? handle.Tail(max) : handle.ReadNew(max);

                return ValueTask.FromResult(Json(new
                {
                    proceso = handle.Id,
                    vivo = handle.Running,
                    codigo = handle.ExitCode,
                    quedan_sin_leer = handle.Unread,
                    descartadas = handle.Dropped,
                    lineas = lines.Select(l => l.ToString()).ToArray(),
                }));
            });

    private static DelegateTool ProcessesTool(IProcessSupervisor supervisor)
        => new(
            name: "procesos",
            description: "Lista los procesos lanzados con su estado, codigo de salida y cuanta salida queda sin leer.",
            parametersJsonSchema: """
            { "type": "object", "properties": {} }
            """,
            handler: (_, _) => ValueTask.FromResult(Json(new
            {
                vivos = supervisor.Running,
                total = supervisor.Total,
                puerta = supervisor.Puerta.IsSet ? "abierta" : "cerrada",
                procesos = supervisor.Handles.Select(h => new
                {
                    proceso = h.Id,
                    vivo = h.Running,
                    codigo = h.ExitCode,
                    comando = h.Command.Command,
                    lineas = h.Captured,
                    sin_leer = h.Unread,
                    segundos = Math.Round(h.Elapsed.TotalSeconds, 1),
                }).ToArray(),
            })));

    private static DelegateTool KillTool(IProcessSupervisor supervisor)
        => new(
            name: "matar",
            description: "Mata un proceso y toda su descendencia.",
            parametersJsonSchema: """
            {
              "type": "object",
              "properties": { "proceso": { "type": "string", "description": "Identificador del proceso." } },
              "required": ["proceso"]
            }
            """,
            handler: (arguments, _) =>
            {
                var id = ReadString(arguments, "proceso");
                var killed = supervisor.Kill(id);
                return ValueTask.FromResult(Json(new
                {
                    proceso = id,
                    matado = killed,
                    nota = killed ? "matado" : "no existe o ya habia terminado",
                }));
            });

    private static DelegateTool WatchTool(IProcessSupervisor supervisor)
        => new(
            name: "observar",
            description: "Registra un patron a vigilar en la salida y vuelve al instante. Cuando aparezca te llegara " +
                         "un evento. Es la forma de monitorizar sin quedarte esperando.",
            parametersJsonSchema: """
            {
              "type": "object",
              "properties": {
                "patron": { "type": "string", "description": "Expresion regular." },
                "proceso": { "type": "string", "description": "Proceso concreto, o vacio para vigilarlos todos." }
              },
              "required": ["patron"]
            }
            """,
            handler: (arguments, _) =>
            {
                var processId = ReadString(arguments, "proceso");
                var watcher = supervisor.Watch(ReadString(arguments, "patron"), processId.Length > 0 ? processId : null);

                return ValueTask.FromResult(Json(new
                {
                    observador = watcher.Id,
                    patron = watcher.Pattern,
                    proceso = watcher.ProcessId ?? "todos",
                    nota = "vigila la salida que llegue a partir de ahora, no la anterior.",
                }));
            });

    private static DelegateTool FlowTool(IProcessSupervisor supervisor)
        => new(
            name: "flujo",
            description: "Cierra o abre la puerta de ejecucion. Con la puerta cerrada, los lanzamientos nuevos " +
                         "quedan en espera sin matar nada de lo que ya corre.",
            parametersJsonSchema: """
            {
              "type": "object",
              "properties": {
                "accion": { "type": "string", "enum": ["pausar", "reanudar"], "description": "Que hacer con la puerta." }
              },
              "required": ["accion"]
            }
            """,
            handler: (arguments, _) =>
            {
                var action = ReadString(arguments, "accion").ToLowerInvariant();
                switch (action)
                {
                    case "pausar":
                        supervisor.Pausar();
                        break;

                    case "reanudar":
                        supervisor.Reanudar();
                        break;

                    default:
                        return ValueTask.FromResult(Json(new { error = "accion no reconocida: usa pausar o reanudar" }));
                }

                return ValueTask.FromResult(Json(new
                {
                    puerta = supervisor.Puerta.IsSet ? "abierta" : "cerrada",
                    vivos = supervisor.Running,
                }));
            });

    /// <summary>El interprete pedido, o el que trae la politica de la sesion.</summary>
    private static ShellKind ResolveShell(JsonElement arguments, IProcessSupervisor supervisor)
    {
        var requested = ReadString(arguments, "shell");
        return requested.Length == 0 ? supervisor.Policy.DefaultShell : ShellCommand.ParseShell(requested);
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string ReadString(JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object &&
           arguments.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : string.Empty;

    private static int ReadInt(JsonElement arguments, string name, int fallback)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => fallback,
        };
    }

    private static bool ReadBool(JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object &&
           arguments.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.True;
}
