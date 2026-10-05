using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

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


    /// <summary>
    /// Pinta un stream en la consola separando razonamiento de respuesta. Sin esto los dos
    /// se mezclan en el mismo hilo de texto y no se sabe que es lo que el modelo esta
    /// contestando y que es lo que esta rumiando.
    /// </summary>
    private sealed class DeltaRenderer
    {
        private readonly SafeStreamingText _thinkingText = new();
        private readonly SafeStreamingText _contentText = new();
        private bool _thinkingOpen;
        private bool _contentOpen;
        private bool _lineStart = true;

        public void Render(StreamDelta delta)
        {
            if (delta.HasThinking)
            {
                if (!_thinkingOpen)
                {
                    CloseContent();
                    Write("(pensando) ", ConsoleColor.DarkGray);
                    _thinkingOpen = true;
                }

                _thinkingText.Write(delta.Thinking, text => Write(text, ConsoleColor.DarkGray));
            }

            if (delta.Content.Length > 0)
            {
                if (!_contentOpen)
                {
                    CloseThinking();
                    _contentOpen = true;
                }

                _contentText.Write(delta.Content, text => Write(text, ConsoleColor.Gray));
            }

            foreach (var toolDelta in delta.ToolCallDeltas)
            {
                // Los argumentos llegan troceados; aqui solo interesa saber que ya se esta pidiendo.
                if (toolDelta.TryGetProperty("function", out var function) &&
                    function.TryGetProperty("name", out var name) &&
                    name.GetString() is { Length: > 0 } toolName)
                {
                    CloseThinking();
                    CloseContent();
                    Write(DataGuard.Display($"(llamando a {toolName})"), ConsoleColor.DarkCyan);

                    // Deja la linea abierta a proposito: la cierra el siguiente bloque
                    // o Close(), y asi la traza del loop no se pega a este aviso.
                    _contentOpen = true;
                }
            }
        }

        public void Close()
        {
            CloseThinking();
            CloseContent();
        }

        private void CloseThinking()
        {
            _thinkingText.Flush(text => Write(text, ConsoleColor.DarkGray));
            if (!_thinkingOpen)
            {
                return;
            }

            _thinkingOpen = false;
            Console.WriteLine();
            _lineStart = true;
        }

        private void CloseContent()
        {
            _contentText.Flush(text => Write(text, ConsoleColor.Gray));
            if (!_contentOpen)
            {
                return;
            }

            _contentOpen = false;
            Console.WriteLine();
            _lineStart = true;
        }

        private void Write(string text, ConsoleColor color)
        {
            foreach (var piece in text.Split('\n').Select((value, index) => (value, index)))
            {
                if (piece.index > 0) { Console.WriteLine(); _lineStart = true; }
                if (piece.value.Length == 0) continue;
                if (_lineStart) { ConsoleUi.Styled(ConsoleUi.StreamMargin, color); _lineStart = false; }
                ConsoleUi.Styled(piece.value, color);
            }
        }
    }


    private static int PrintHelp()
    {
        ConsoleUi.Section("AYUDA  /  Comandos y opciones");
        ConsoleUi.Text(
            """
            AgentLoop - harness dirigido por eventos sobre NVIDIA

            Uso:
              dotnet run --project src/AgentLoop.Cli -- <comando> [opciones]
              dotnet run --project src/AgentLoop.Cli (sin argumentos: abre el CLI interactivo)

            Comandos:
              chat "<prompt>"       Una llamada suelta. Verifica capa de comunicacion + capa de parseo.
              discover              Sondea el catalogo y reporta 3 modelos con capacidades agenticas.
              run "<objetivo>"      Alias de goal: reactor por eventos con tarjetas y herramientas.
              run-chat "<prompt>"   Utilidad anterior de tool calling por turnos.
              goal "<objetivo>"     Operacion dirigida por eventos: tarjetas de estado y terminales
                                    (bash, sh, powershell, pwsh, cmd). Ver la seccion Operaciones.
              selftest              Valida el harness completo sin tocar la red.
              selftest-coordinator Verifica admision, evidencia y controles del actor.
              project-admit         Valida un contrato de proyecto sin LLM ni ejecucion.
              project-run           Construye entregables por eventos con perfil explicito.
              selftest-projects     Verifica contratos y actor con backend simulado, sin red.
              project-check-backend Preflight del adaptador; --command ejecuta un check del perfil.
              project-inspect       Inspecciona estado durable sin repetir acciones.
              project-reconcile     Registra una decision del operador sobre una accion incierta.
              recuperacion <jsonl>  Inspecciona eventos y acciones pendientes tras interrupcion.
              selftest-events       Verifica bus, senales, procesos y reactor sin proveedor remoto.
              diagnostico [runId]   Diagnostico post-mortem de un journey persistido (solo lee disco).
              interactive           CLI con reactor de eventos y memoria (lo que usa F5 en
                                    Visual Studio). Dentro: nuevo, historial, vars, set, unset,
                                    sistema, plantilla, pensar, stream, salir.

            Opciones:
              --no-color            Desactiva colores y sombreado; tambien respeta NO_COLOR.
              --plain-ui            Presentacion sin paneles ni colores.
              --model <id>          Modelo a usar (por defecto: el primero de artifacts/agentic-models.json).
              --target <n>          discover: cuantos modelos agenticos buscar (3).
              --max-candidates <n>  discover: cuantos modelos sondear como maximo (30).
              --max-iterations <n>  run-chat: tope de iteraciones del loop (12).
              --deadline <s>        goal: presupuesto de la operacion en segundos (300).
              --max-ciclos <n>      goal: presupuesto de llamadas al modelo (80).
              --project-contract <json> Contrato versionado de proyecto.
              --project-profile <json> Perfil externo admitido por el host.
              --fixture --fixture-root <ruta> Cliente determinista con archivos externos.
              --evidence-dir <ruta> Almacen de evidencia del host.
              --resume-journal <jsonl> Recupera estado sin repetir efectos inciertos.
              --action-id <id> --decision applied|not-applied --note "<texto>"  Reconciliacion explicita.
              --artifact <ruta>    goal: fija el archivo que debe observarse y comprobarse.
              --contains <texto>   goal: contenido obligatorio del artefacto.
              --json               goal: exige JSON valido en el artefacto.
              --allow-existing     goal: permite un artefacto sin cambios respecto al inicio.
              --max-procesos <n>    goal: procesos simultaneos permitidos (4).
              --shell <interprete>  goal: interprete por defecto (bash, sh, powershell, pwsh, cmd).
              --cwd <ruta>          goal: directorio de trabajo de los comandos.
              --no-shell            Deshabilita comandos arbitrarios en operaciones.
              --scope-root <ruta>   Acota archivos a una raiz; tambien bloquea shell arbitrario.
              --politica <modo>     goal: auto (por defecto), confirmar o simular.
              --system "<texto>"    Plantilla de prompt de sistema con huecos {{variable}}.
              --system-file <ruta>  La misma plantilla, leida de un archivo.
              --var clave=valor     Define un parametro de la plantilla. Repetible.
              --thinking <modo>     auto (por defecto), on u off. Ver la seccion Thinking.
              --thinking-budget <n> Tokens que puede gastar razonando (256).
              --stream              Pinta la respuesta segun llega. --no-stream la apaga
                                    (el CLI interactivo la trae puesta).

            Plantillas:
              El prompt de sistema se interpola con {{variable}}. El modo de escapado va
              en el hueco: {{xml:campo}} para meter texto dentro de un bloque etiquetado,
              {{json:campo}} dentro de una cadena JSON, {{raw:campo}} sin tocar.
              Interpolar sin escapar es como concatenar SQL: un valor puede cerrar el
              bloque y colar instrucciones. Si falta una variable, el comando falla antes
              de llamar al proveedor en vez de mandar un {{hueco}} literal.

              Ya definidas: modelo, herramientas, fecha, hora, idioma, contexto.

              Ejemplo:
                dotnet run --project src/AgentLoop.Cli -- run-chat "Resume esto" \
                  --system "Eres {{rol}}. Responde en {{idioma}}. <d>{{xml:dato}}</d>" \
                  --var rol=analista --var "dato=<ACME> & socios"

            Operaciones (goal):
              No hay turnos. Hay un objetivo, un bus de eventos y un supervisor de procesos.
              El agente declara donde esta jugando tarjetas de estado (abrir, avanzar,
              comprobar, cerrar, abortar) y el sistema lleva el libro mayor: son transacciones,
              se pueden anidar, y la operacion NO cuenta como cumplida hasta que se juega la
              tarjeta de cierre. Ni el reloj ni una respuesta bonita la cierran.

              Lo corto se ejecuta con ejecutar; lo largo se arranca con lanzar y se sigue con
              esperar, leer y observar. Las esperas son sobre reset events, asi que el agente
              nunca bloquea un hilo: mientras duerme, los procesos hijos siguen volcando salida
              y cada linea, patron o final publica un evento que lo despierta.

              Con --politica simular no se ejecuta nada y se responde que habria pasado; con
              confirmar, los efectos requieren aprobacion: s/c/N con goal y aprobar [id] [continuo]/rechazar [id] en interactive.
              permisos lista autorizaciones de sesion; permisos revocar las elimina. Destructivos/opacos requieren permiso puntual aun en auto.
              code_view/read_text_file deben leer antes de edit_text_file o sobrescribir; se exige read_receipt vigente.
              ejecutar/lanzar admiten saltos reales, here-strings PowerShell y heredocs bash/sh. Hay una lista de comandos
              irreversibles vetada siempre. No es un sandbox: evita el accidente, no contiene
              a nadie decidido.

              Ejemplo:
                dotnet run --project src/AgentLoop.Cli -- goal "Cuenta los .cs del repo y deja constancia del numero" \
                  --deadline 180 --politica auto --stream

            Thinking:
              El razonamiento explicito viaja como {"type":"enabled","budget_tokens":n} en
              la raiz de la peticion y vuelve tipado en ThinkingTrace: bloques, origen
              (reasoning_content, bloques de content, <think> en linea o solo coste) y
              tokens gastados.

              Con --thinking auto va activo salvo en los modelos que el ultimo 'discover'
              marco sin soporte. Si un modelo rechaza el parametro, la peticion se reintenta
              una vez sin el y ese modelo queda apuntado para el resto de la sesion, asi que
              no se pierde ni un turno por pedirlo.

              'discover' sonda la capacidad 'thinking' y la guarda en el artefacto: correrlo
              es lo que hace que 'auto' sepa de que modelos fiarse.

              Pensar gasta del mismo max_tokens que la respuesta, asi que con thinking activo
              el harness sube el tope para que quede margen para contestar.

            Streaming:
              Con --stream la respuesta llega por SSE y se pinta mientras el modelo la
              escribe: el razonamiento en gris con el prefijo (pensando) y la respuesta
              en texto normal, para no confundir lo que rumia con lo que contesta.

              Los chunks se acumulan y se rearman en una respuesta con la misma forma que
              la no-streaming, asi que el thinking, las tool calls y el consumo salen
              tipados igual por los dos caminos; el selftest comprueba justo esa
              equivalencia. Las tool calls llegan troceadas (el id y el nombre solo en el
              primer trozo, los argumentos a cachos) y se funden por su indice.

              El consumo solo viaja en el stream si se pide con stream_options.include_usage,
              que el harness manda siempre que hay stream; si el backend lo ignora, se avisa
              en vez de reportar cero tokens.

            La API key NVIDIA se lee de local-secrets/nvidia-api-key.txt, o de la
            variable de entorno NVIDIA_API_KEY.
            """);
        return 0;
    }

}
