# Análisis del proyecto y del fallo al crear un poema

Revisión del 27 de septiembre de 2026 sobre el commit `b977a8293227be9f8029b8ce41c06ce94a37dd0e`, en Windows y con .NET SDK 9.0.316. Se revisó el código, se compiló, se ejecutó la suite existente, se hizo una ejecución real con el modelo configurado y se reprodujeron defectos del harness mediante una sonda sin red.

**El bloqueo actual confirmado está en el proveedor:** TokenRouter responde HTTP 503, `model_not_found`, con “No available channel for model z-ai/glm-5.3-free under group default (distributor)”. El modelo tampoco figura en el catálogo consultado durante esta revisión. La ejecución falla antes de recibir herramientas o producir un archivo. Además, existen defectos del harness que afectan el cierre y la evidencia aunque el proveedor vuelva a responder.

## Qué es el proyecto

`AgentLoop` es una aplicación de consola en C# para ejecutar agentes mediante un modelo remoto. El proyecto apunta a `net9.0` y no declara paquetes NuGet externos. Usa HTTP y JSON para hablar con un gateway compatible con Chat Completions, interpreta respuestas y llamadas a herramientas, y avanza mediante máquinas de estados.

| Componente | Función |
|---|---|
| `Program.cs` | CLI, sesión interactiva, configuración y selección de modos. |
| `Provider/`, `Parsing/`, `Discovery/` | Cliente de TokenRouter, reintentos, streaming, razonamiento, parser y sondeo de capacidades. |
| `StateMachines/`, `Looping/` | Máquinas de estados y bucle genérico con condiciones de terminación. |
| `Goals/`, `Cards/`, `Events/` | Reactor por eventos, objetivo, tarjetas transaccionales y libro mayor. |
| `Tools/`, `Terminals/` | Ejecución de herramientas y procesos reales, lectura de salidas y control de tiempos. |
| `Harness/` | Composición, ejecución de runs, concurrencia, observabilidad y snapshots/journeys. |

En una operación, el modelo debe solicitar acciones reales y completar las tarjetas `abrir`, `comprobar` y `cerrar`. `avanzar` permite registrar pasos y `abortar` declara un fracaso. Escribir texto en la respuesta no crea un archivo. La herramienta `ejecutar` permite escribirlo mediante el shell; después debe leerse y comprobarse. El manifiesto registra existencia, bytes y un hash abreviado del archivo.

Los modos tienen capacidades distintas:

| Entrada | Herramientas efectivas | Evidencia persistida automáticamente |
|---|---|---|
| `chat` | Ninguna | No |
| `run` / `interactive` | `add_numbers`, `get_time` | No |
| `goal` / `lobos` | Tarjetas, shell y supervisor de procesos | Imprime eventos en consola; no guarda Journey/snapshot |
| `journal` | Tarjetas, shell y supervisor de procesos | Guarda JSON y Markdown, con carencias de instrumentación descritas abajo |

## Ejecución real: causa inmediata

Se pidió un poema **original**, de cuatro versos, para comprobar creación y evidencia sin depender de encontrar una obra concreta en Internet. Se usó un directorio exclusivo de esta revisión.

```powershell
$poemWork = Join-Path (Get-Location) 'docs/evidence/2026-09-27/remote-workspace'
New-Item -ItemType Directory -Force -Path $poemWork | Out-Null
dotnet ./bin/Debug/net9.0/AgentLoop.dll goal 'Escribe un poema original de cuatro versos sobre la lluvia en notes/poema-original.md. Lee el archivo creado para comprobar que existe, contiene el poema y tiene cuatro versos. Deja evidencia real y cierra la operacion con las tarjetas correspondientes.' --model z-ai/glm-5.3-free --deadline 180 --max-ciclos 12 --max-tokens 2048 --cwd $poemWork
```

Resultado observado: **salida 1, estado `Fallido`, tres ciclos, 28 segundos, cero herramientas, cero tarjetas y cero procesos**. El archivo no se creó. Las tres llamadas lógicas terminaron con el error del gateway; hubo reintentos internos del cliente. Este resultado no permite evaluar la capacidad creativa ni el seguimiento de instrucciones del modelo, porque no se obtuvo una respuesta suya.

Evidencia: [registro completo de la ejecución](evidence/2026-09-27/remote-goal.txt) y [comprobación del catálogo](evidence/2026-09-27/provider-catalog.json). `GET /v1/models` respondió 200 con 148 IDs y sin `z-ai/glm-5.3-free`. Esto demuestra la ausencia en el catálogo consultado y la falta de canal para esta cuenta/grupo en ese momento; no demuestra una retirada definitiva para todos los usuarios.

La selección usa datos antiguos: `artifacts/agentic-models.json` se generó el **11 de septiembre**, y `ResolveModel` toma su primer modelo sin comprobar antigüedad ni disponibilidad (`Program.cs:2100`). Si no hay artefacto, el fallback es el mismo ID (`Provider/TokenRouterOptions.cs:13`). `lobos` sí tiene una comprobación previa; `goal` y `journal` no la comparten. El reactor clasifica cualquier 503 como transitorio, incluso cuando el cuerpo dice que no hay modelo/canal.

El catálogo muestra otro ID con `free` en el nombre. No se probó su inferencia: el nombre por sí mismo no garantiza acceso gratuito, y el sondeo histórico de ese ID había recibido 403 por cuota.

## Defectos del harness confirmados

### 1. El límite de ciclos descarta la última acción solicitada

`ReasonAsync` incrementa `context.Cycles` antes de llamar al modelo (`Goals/EventDrivenAgent.cs:346`). Al recibir herramientas, cambia a `Despachando` (`:375`); su ejecución ocurre en la siguiente iteración. Sin embargo, `BreakWhen` comprueba inmediatamente `Cycles >= maxCycles` (`Program.cs:900`, `Harness/Runner/GoalRunner.cs:72`). Por ello termina entre la recepción y la ejecución de las herramientas del último ciclo.

La sonda devuelve exactamente tres respuestas: `abrir`, `comprobar`, `cerrar`.

| Límite | Peticiones realmente realizadas | Tarjetas ejecutadas | Resultado |
|---|---:|---|---|
| 3 ciclos | 3 | `abrir`, `comprobar` | Fallo; el `cerrar` recibido se descarta |
| 4 ciclos | 3 | `abrir`, `comprobar`, `cerrar` | Éxito |

Evidencia: primeros dos casos de [harness-probe.json](evidence/2026-09-27/harness-probe.json). Se utiliza el `GoalRunner` real con un cliente de respuestas deterministas. Es una reproducción del defecto del harness, no una ejecución del modelo remoto ni una creación de poema.

La condición externa `Expired` puede interrumpir en esa misma frontera, aunque el reactor intenta permitir terminar un despacho ya decidido. El presupuesto debe impedir nuevas llamadas al modelo y permitir procesar las acciones ya recibidas bajo sus límites correspondientes.

### 2. El modo interactivo no puede guardar el poema

`BuiltInTools.DefaultRegistry()` solo registra suma y hora (`Tools/BuiltInTools.cs:71`). Lo usan `InteractiveAsync` y `RunAsync` (`Program.cs:63`, `:728`). No exponen shell ni escritura de archivos.

Además, escribir `goal ...`, `journal ...` o `lobos ...` dentro del prompt interactivo no invoca esos comandos: la lista de utilidades reconocidas no los incluye (`Program.cs:137`), por lo que la línea completa se envía como texto al agente de suma/hora (`:143`). El perfil inicial de Visual Studio abre precisamente `interactive` (`Properties/launchSettings.json`).

Si el fallo se observó desde F5 o desde ese prompt, existe una causa adicional independiente del proveedor: ese camino no dispone de la herramienta necesaria. Para operar con archivos, actualmente debe invocarse `goal` o `journal` como subcomando del proceso desde la terminal.

### 3. La evidencia no se registra igual en todos los modos

`GoalAsync` usa `BuildWithBusHook` para imprimir eventos y retorna sin guardar snapshot ni Markdown (`Program.cs:864`). `lobos` delega en ese método. La persistencia implementada en `GoalRunFactory.RunWithRunnerAsync` se usa desde `journal` (`Harness/Composition/GoalRunFactory.cs:199`). Por ello `diagnostico last` puede leer una ejecución anterior aunque se acabe de ejecutar otro `goal`.

Incluso en `journal`, `Journey.ModelCalls` y `Journey.ToolCalls` cuentan objetos `ModelStep` y `ToolStep` (`Harness/Core/Journey.cs:28`). El runner registra eventos del bus y una transición final, pero no esos pasos. `Trace` se envía a `OnLog`, que solo reenvía a observadores; el runner entrega una lista vacía (`Harness/Runner/GoalRunner.cs:46`, `:54`; `Harness/Steps/StepRecorder.cs:86`).

La sonda confirma **tres peticiones y tres tarjetas ejecutadas**, mientras Journey reporta **cero llamadas al modelo, cero herramientas y cero métricas**. Estos contadores no sirven actualmente como prueba de que no hubo ejecución. El registro histórico se identifica como simulado por sus eventos `source=scripted` y su resultado “Smoke test del protocolo”, además de los contadores.

### 4. El objetivo del modelo puede eludir la validación del archivo

Los criterios se derivan de `transaction.Objective`, tomado del campo `objetivo` enviado por el modelo al abrir una tarjeta (`Cards/CardLedger.cs:342`, `:392`). No se fijan a partir del objetivo original del usuario.

La sonda usa un objetivo original con `notes/poema-inexistente.md` y no crea ningún archivo:

| Texto de la tarjeta de apertura | ¿Existe el archivo? | ¿Acepta cerrar? |
|---|---|---|
| Conserva la ruta del objetivo original | No | No, correctamente |
| Lo resume como “Crear un poema” | No | **Sí: `GoalMet=true`, sin entradas de evidencia** |

Esto permite falsos éxitos. No es la causa del 503 observado, pero impide confiar en `CUMPLIDO` como garantía de que se satisfizo la petición original. Incluso conservando la ruta, la verificación actual comprueba bytes/existencia; no valida que sean un poema, cuatro versos o una transcripción contrastada con una fuente.

## Otras inconsistencias relevantes

- **`journal` ejecuta con otra configuración.** Analiza `--stream` y la política de thinking, pero no los transmite al reactor; tampoco conecta `--max-tokens` ni `--cwd` al runner (`Program.cs:954`). El runner queda con `MaxTokens=900` y `Thinking=null`; `goal` usa 2048 y una política explícita. La sonda confirma los 900 tokens y el campo nulo. Es una diferencia real entre modos; no se pudo medir su impacto en el modelo porque el proveedor rechazó las peticiones.
- **`--thinking off` omite el campo.** `ThinkingPreference.Never` devuelve `null`, aunque existe `ThinkingOptions.Off` con `type=disabled` (`Provider/Thinking.cs:20`, `:112`). No equivale a pedir desactivación explícita al backend. No se afirma que el backend actual ignore una desactivación: no hubo respuesta de inferencia para comprobarlo.
- **`lobos` anuncia límites diferentes a los que ejecuta por defecto.** Imprime 60 segundos y 5 ciclos (`Program.cs:1029`), pero delega sin insertar esos valores; `GoalAsync` aplica 300 segundos y 80 ciclos si faltan los flags. Los valores explícitos sí se propagan.
- **El ejemplo `lobos` no consiste solo en crear un poema original.** Pide el cántico inicial de un doblaje concreto y contrastarlo con una fuente real (`Program.cs:1018`). Añade búsqueda y comprobación de una obra existente. El documento histórico describe dificultades de investigación y cierre, pero los journeys reales que permitirían verificar esas corridas no están en esta copia del repo.

La explicación anterior de `docs/diagnostico-no-cierre.md` que atribuye el problema al comportamiento del modelo no basta para el estado actual: hay un rechazo del gateway verificable y defectos locales reproducibles. Tampoco puede garantizarse que aumentar a 40 ciclos resuelva esos problemas.

## Evidencia entregada y alcance

## Estado antes y después del fix

El estado previo quedó capturado en [before-fix-commit.txt](evidence/2026-09-27/before-fix-commit.txt), [before-fix-status.txt](evidence/2026-09-27/before-fix-status.txt) y [before-fix.patch](evidence/2026-09-27/before-fix.patch). El commit de partida fue `b977a829...`; el parche previo estaba vacío porque los cambios del informe anterior eran archivos nuevos, y el estado se conserva por separado.

Se aplicaron estos cambios:

- NVIDIA NIM como transporte por defecto (`https://integrate.api.nvidia.com/v1`), modelo `z-ai/glm-5.3`, `NVIDIA_API_KEY` con compatibilidad de lectura del secreto legado, timeout de cinco minutos y `NVIDIA_BASE_URL` opcional.
- `chat_template_kwargs.enable_thinking`/`clear_thinking` para GLM/NIM; los campos de transporte no se agregan al payload.
- Un descubrimiento histórico que todavía contiene `:free` ya no puede seleccionar ese modelo como predeterminado.
- El ciclo límite permite despachar las tools recibidas en el último ciclo. La sonda posterior confirma que con tres ciclos se ejecutan las tres tarjetas.
- La aceptación de archivos usa siempre el objetivo original del usuario; cambiar el texto de la tarjeta de apertura ya no elimina el requisito.
- `journal` transmite `cwd`, `max-tokens` y la política de thinking; los Journeys registran llamadas de modelo y de herramienta con duración, tokens y resultado.
- El modo interactivo enruta `goal`, `journal`, `lobos` y `diagnostico` como subcomandos reales.

La verificación posterior está en [after-fix-build.txt](evidence/2026-09-27/after-fix-build.txt), [after-fix-selftest.txt](evidence/2026-09-27/after-fix-selftest.txt) y [after-fix-probe.json](evidence/2026-09-27/after-fix-probe.json). Compila sin errores, la suite termina en `SELF-TEST OK`, la sonda de tres ciclos cierra correctamente, reporta 3 llamadas de modelo y 3 de herramienta, y mantiene bloqueado el cierre cuando falta el archivo aunque el modelo cambie el objetivo de su tarjeta.

La consulta al catálogo NVIDIA devolvió 200 y confirmó `z-ai/glm-5.3` (`nvidia-catalog-after-fix.json`). El primer ping de inferencia devolvió 401 porque la credencial que existía en `local-secrets/tokenrouter-api-key.txt` era de TokenRouter; el proveedor estaba migrado en código, pero la aplicación aún priorizaba ese archivo legado.

Después se probó una credencial NVIDIA válida en memoria. La aplicación inicialmente seguía priorizando el archivo legado; se corrigió esa precedencia para que `NVIDIA_API_KEY` gane sobre cualquier secreto antiguo. La ejecución posterior terminó en `CUMPLIDO`: 7 ciclos, 7 herramientas, 4 tarjetas (`abrir`, `avanzar`, `comprobar`, `cerrar`), 3 procesos y 0 tarjetas rechazadas. El poema quedó en `nvidia-poem-workspace/notes/poema-original.md`, con 4 líneas, 116 bytes y SHA-256 `254A2E05B004D05B53C8F280DB81E6FE03980214ACD57E8E579983F79B81136B`. La respuesta consumió 49.620 tokens, incluidos 3.380 de razonamiento. Ver [ejecución NVIDIA exitosa](evidence/2026-09-27/nvidia-poem-run.txt) y [evidencia del archivo](evidence/2026-09-27/nvidia-poem-evidence.json).

La clave quedó guardada en `local-secrets/nvidia-api-key.txt`, ruta ignorada por Git y documentada como secreto dedicado. El comando de chat por consola se probó con `--thinking off`; NVIDIA respondió `stop`, backend `z-ai/glm-5.3` y `CHAT_NVIDIA_OK`. La salida enmascara la clave. Ver [evidencia del chat](evidence/2026-09-27/chat-nvidia-console.txt).

El catálogo de modelos NVIDIA del host ahora contiene `z-ai/glm-5.3` y `moonshotai/kimi-k3`. En la consola interactiva se pueden listar con `modelos` y cambiar con `modelo kimi`, `modelo glm` o el ID completo; el cambio se aplica al siguiente turno y no reinicia el cliente HTTP.

| Archivo | Contenido |
|---|---|
| [audit-summary.json](evidence/2026-09-27/audit-summary.json) | Commit, entorno, resultados y existencia de archivos. |
| [build.txt](evidence/2026-09-27/build.txt) | Compilación correcta: cero errores, dos advertencias de nulabilidad. |
| [selftest.txt](evidence/2026-09-27/selftest.txt) | `SELF-TEST OK`, salida 0 y 295 marcas `[ok]`. |
| [remote-goal.txt](evidence/2026-09-27/remote-goal.txt) | Ejecución real fallida, con la línea de credencial omitida. |
| [provider-catalog.json](evidence/2026-09-27/provider-catalog.json) | Consulta actual del catálogo, sin credenciales. |
| [harness-probe.json](evidence/2026-09-27/harness-probe.json) | Reproducciones deterministas de límite, observabilidad y aceptación. |
| [HarnessProbe.cs.txt](evidence/2026-09-27/HarnessProbe.cs.txt) | Código de la sonda; extensión que evita incorporarlo a la aplicación. |
| [run-probe.ps1](evidence/2026-09-27/run-probe.ps1) | Compila y ejecuta la sonda en un directorio temporal. |
| [evidence-manifest.json](evidence/2026-09-27/evidence-manifest.json) | Rutas, tamaños y SHA-256 completos de este informe y los archivos de evidencia. |
| [after-fix-probe.json](evidence/2026-09-27/after-fix-probe.json) | Reproducción después del fix: cierre y observabilidad corregidos. |
| [nvidia-catalog-after-fix.json](evidence/2026-09-27/nvidia-catalog-after-fix.json) | Catálogo NVIDIA actual: 200 y modelo configurado listado. |
| [nvidia-ping-after-fix.json](evidence/2026-09-27/nvidia-ping-after-fix.json) | Ping NVIDIA: 401 con la credencial anterior, sin guardar la clave. |
| [nvidia-poem-run.txt](evidence/2026-09-27/nvidia-poem-run.txt) | Ejecución real exitosa con NVIDIA y el harness corregido. |
| [nvidia-poem-evidence.json](evidence/2026-09-27/nvidia-poem-evidence.json) | Existencia, tamaño, cuatro líneas y SHA-256 del poema. |
| [chat-nvidia-console.txt](evidence/2026-09-27/chat-nvidia-console.txt) | Prueba del chat LLM desde consola con clave en su ruta dedicada. |

Para reproducir la comprobación local:

```powershell
dotnet build ./AgentLoop.csproj --nologo
dotnet ./bin/Debug/net9.0/AgentLoop.dll selftest
./docs/evidence/2026-09-27/run-probe.ps1
```

No existe `notes/wolf-rain-poem.md` en esta copia, ni el poema del ensayo remoto. El único Journey que ya existía corresponde a una simulación del 11 de septiembre. Las pruebas locales que pasan no cubren las fronteras reproducidas aquí y no verifican disponibilidad remota. Los archivos de esta entrega documentan el diagnóstico; no constituyen evidencia de un poema completado.

## Orden propuesto de corrección

1. Resolver un modelo con acceso y canal operativo para esta cuenta; validar disponibilidad reciente y clasificar `model_not_found` antes de reintentar como un 503 genérico.
2. Corregir el corte de ciclos para despachar las herramientas ya recibidas, incluyendo un cierre solicitado en el último ciclo permitido.
3. Unificar `goal`, `lobos` y `journal` en una ejecución con las mismas opciones y persistencia, y enrutar esos subcomandos desde el modo interactivo.
4. Registrar llamadas, respuestas, herramientas, códigos de salida, motivo de término y consumo real como pasos persistidos.
5. Fijar los criterios del archivo desde el objetivo original y comprobar el contenido requerido antes de aceptar el cierre.
6. Volver a ejecutar el poema original hasta obtener archivo leído, validación, cierre y Journey concordantes; evaluar la recuperación de una obra existente como una tarea adicional.
