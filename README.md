# AgentLoop

Consola en C# / .NET 9 para ejecutar objetivos con NVIDIA NIM, herramientas
locales y tarjetas transaccionales. No requiere paquetes NuGet externos.

El motor principal es `EventOperationCoordinator`: los eventos habilitan
handlers; el modelo propone acciones; el sistema admite cada herramienta y
comprueba la evidencia antes de aceptar el cierre. Las esperas usan senales
asincronas, incluida la puerta de pausa con `AsyncManualResetEvent`.

## Arranque

```powershell
dotnet build
dotnet run -- interactive
```

F5 usa el perfil `cli interactivo` de [launchSettings.json](Properties/launchSettings.json).
Credencial: [local-secrets/README.md](local-secrets/README.md).
Endpoint predeterminado: `https://integrate.api.nvidia.com/v1`.
Modelos configurados: `z-ai/glm-5.3` y `moonshotai/kimi-k3`; incluir un ID
en la lista no garantiza acceso o disponibilidad del proveedor.

## Entradas y capacidades

| Entrada | Ejecutor y alcance | Evidencia |
| --- | --- | --- |
| Texto libre / `run` en `interactive` | Actor por eventos, 17 tools, tarjetas, memoria | Journal y resumen de operacion en `.agentloop/` |
| `run`, `goal`, `lobos` independientes | Actor por eventos, archivos y procesos | Journal en `.agentloop/event-journal/` |
| `journal` | `GoalRunner` sobre el actor | Journal y snapshot/Journey en `artifacts/journeys/` |
| `chat` independiente | Una llamada sin herramientas | Consola |
| `chat` dentro de `interactive`, `run-chat` independiente | Utilidad conversacional anterior con tools basicas | Hilo del chat interactivo; no acredita tarjetas |
| `discover` | Sondeo remoto de capacidades | Cache `artifacts/agentic-models.json` |
| `diagnostico`, `recuperacion` | Lectura de evidencia | Informe; no repiten efectos |

En el CLI: `modelos`, `modelo glm`, `modelo kimi` cambian el modelo de la
siguiente operacion. Durante texto libre/`run`: `estado`, `pausa`, `reanudar`,
`cancelar`, `aprobar` y `rechazar` publican controles independientes.

```powershell
dotnet run -- goal "Crea y lee un poema de cuatro versos sobre Mexico en poema.txt" --artifact poema.txt --contains Mexico
dotnet run -- goal "Crea y lee resultado.json" --artifact resultado.json --json
dotnet run -- recuperacion ".agentloop/event-journal/<runId>.jsonl"
```

Las herramientas se componen en `GoalRunFactory` y se filtran por `Scope`:

- Datos y archivos: `add_numbers`, `get_time`, `write_text_file`,
  `read_text_file`, `list_directory`, `get_system_context`, `where_command`, `find_files`.
- Operaciones: `tarjeta`, `ejecutar`, `lanzar`, `esperar`, `leer`, `procesos`,
  `matar`, `observar`, `flujo`.

Para efectos: abrir tarjeta, ejecutar, observar un resultado exitoso, usar su
`receipt_id` como `evidencia_id` al comprobar y cerrar. Una respuesta textual
no crea archivos. Simular no produce evidencia de efectos reales.
Los criterios comprueban observacion, frescura, hash y contenido configurado;
la calidad literaria o correccion semantica requiere validadores adicionales.

## Seguridad de datos

[DTOs, sanitizacion y limites de confianza](docs/seguridad-datos-harness.md).
Para archivos acotados: `interactive --no-shell --scope-root "C:\proyectos\demo"`.
El modo auto con shell conserva los permisos del usuario; no es un sandbox.

## Mapa del codigo

| Ruta | Responsabilidad |
| --- | --- |
| `Program.cs` | CLI, sesion, controles y opciones |
| `Goals/`, `Cards/`, `Events/` | Actor, admision, aceptacion, contratos, tarjetas y bus |
| `Tools/`, `Terminals/`, `Concurrency/` | Tools, procesos, capacidad y reset events |
| `Security/` | DTOs de datos, validacion, sanitizacion, secretos y recursos protegidos |
| `Provider/`, `Parsing/`, `Discovery/`, `Configuration/` | HTTP NVIDIA, SSE, reasoning, modelos y credenciales |
| `Harness/` | Composicion, Journey, journal, observacion y utilidades de soporte |
| `StateMachines/`, `Looping/` | Conversacion y compatibilidad; no gobiernan operaciones principales |
| `Prompting/` | Plantillas, variables y sanitizacion |
| `docs/`, `scripts/` | Guias, informes, evidencia e inventario |

El puerto activo es `ILLMClient`; los detalles NVIDIA viven en
[Provider/Nvidia](Provider/README.md). Sondas historicas requieren su revision de origen.

## Estado comprobado y documentacion

Arquitectura vigente y flujos ASCII: [harness por eventos](docs/harness-eventos.md).
Incidente y correccion POS: [informe](docs/incidentes/pos-planeacion-sin-ejecucion-2026-10-01.md).
Validacion de recuperacion POS: [evidencia](docs/evidence/2026-10-01/README.md).
Validacion vigente de seguridad: [evidencia](docs/evidence/2026-10-01-seguridad/README.md).

Validacion local anterior del 1 de octubre: 382 comprobaciones correctas, 68 del
coordinador; compilacion con 0 errores y 0 advertencias. Sin nueva inferencia remota.

Validacion anterior registrada el 30 de septiembre de 2026, commit `8b20a3f`:
compilacion sin errores ni advertencias, 340 comprobaciones correctas y
prueba NVIDIA de poema con escritura, lectura, comprobacion y cierre.
No equivale a una comprobacion nueva de disponibilidad remota.

## Proyectos verificables por eventos

[Guia y flujo ASCII](docs/goals-proyectos.md). P0-P5 implementadas y P6 con
fixture POS aislado real. La suite del 3 de octubre tiene **415 comprobaciones**.
[Resultados del fixture y NVIDIA](docs/evidence/2026-10-03-proyectos/README.md).

```powershell
docker build -t agentloop-project-node:1 -f ProjectAssets/Dockerfile ProjectAssets
dotnet run -- project-admit --project-contract "examples/project-goals/pos-minimo.example.json"
dotnet run -- project-run --project-contract "examples/project-goals/pos-minimo.example.json" --fixture
```

Para NVIDIA, usar un contrato con root propio y omitir `--fixture`. El host exige
validacion funcional antes de cerrar; fallos del modelo o presupuestos agotados
no se declaran exito. La imagen preparada requiere Docker Linux; ejecucion offline,
perfiles fijos, archivos declarados, contexto acotado y recuperacion por journal.
`must-change` compara contra la baseline: usar una raiz nueva para repetir el fixture.

[Plan de implementacion y estado](docs/plan-goals-proyectos-pos-2026-10-02.md).

[Indice documental](docs/README.md) ·
[Arquitectura y pendientes](docs/implementacion-coordinador-eventos-2026-09-30.md) ·
[Inventario y limpieza](docs/limpieza-datos-repo-2026-09-30.md) ·
[Indice de evidencia](docs/evidence/README.md)

Comprobaciones existentes: `dotnet run -- selftest` o `dotnet run -- selftest-coordinator`.
Inventario documental: `powershell -File scripts/audit-repository.ps1`.
