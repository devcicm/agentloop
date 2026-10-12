# AgentLoop

[Reorganizacion aplicada, matriz y flujo ASCII](docs/architecture/reorganizacion-implementada.md).

Consola en C# / .NET 9 para ejecutar objetivos con NVIDIA NIM, herramientas
locales y tarjetas transaccionales. No requiere paquetes NuGet externos.

## Proposito

Este repo construye un harness reutilizable para coordinar agentes, herramientas,
procesos, contexto y evidencia mediante eventos. El modelo propone acciones;
el host valida y ejecuta, y las tarjetas exigen comprobacion antes del cierre.
Core/Kernel conservan contratos y estado independientes del proveedor y del
dominio. Los proyectos de negocio se definen mediante contratos y perfiles externos.

[Proposito, alcance y limites](docs/proposito-y-alcance.md) Â·
[Diagramas de flujo operativos](docs/architecture/flujos-operativos.md) Â·
[Plan de publicacion](docs/plans/publicacion-github-2026-10-07.md).

## Candidato de version

`0.1.0-alpha.1`: preview local. SDK fijado en global.json; version comun en
Directory.Build.props. Se distribuye bajo licencia MIT; la migracion a .NET 10
sigue pendiente antes de una entrega estable.

[Instalacion y uso desde ZIP](docs/guides/instalacion.md) Â·
[Changelog](CHANGELOG.md) Â· [Contribuir](CONTRIBUTING.md) Â· [Seguridad](SECURITY.md).

```powershell
dotnet run --project src/AgentLoop.Cli -- version
```

El empaquetado requiere fuentes registradas en Git. `scripts/package-release.ps1`
genera solo archivos locales, sin tags/push/releases. Con licencia pendiente,
`-AllowUnlicensedPreview` permite preparar un candidato interno; la preparacion
normal de distribucion queda bloqueada. Los workflows no publican releases.

El motor principal es `EventOperationCoordinator`: los eventos habilitan
handlers; el modelo propone acciones; el sistema admite cada herramienta y
comprueba la evidencia antes de aceptar el cierre. Las esperas usan senales
asincronas, incluida la puerta de pausa con `AsyncManualResetEvent`.

## Arranque

[Evidencia del candidato local](docs/evidence/2026-10-07-candidato-local/README.md):
build en clon aislado, 474 comprobaciones, ZIP auditado y prueba NVIDIA.

```powershell
dotnet build
dotnet run --project src/AgentLoop.Cli -- interactive
```

F5 usa el perfil `cli interactivo` de [launchSettings.json](src/AgentLoop.Cli/Properties/launchSettings.json).
Credencial: [local-secrets/README.md](local-secrets/README.md).
Endpoint predeterminado: `https://integrate.api.nvidia.com/v1`.
Modelos configurados: `z-ai/glm-5.3` y `moonshotai/kimi-k3`; incluir un ID
en la lista no garantiza acceso o disponibilidad del proveedor.

[Presentacion de consola y opciones visuales](docs/guides/consola-ui.md).
[Comandos multilinea, permisos y edicion con lectura previa](docs/guides/comandos-permisos-edicion.md).

## Entradas y capacidades

| Entrada | Ejecutor y alcance | Evidencia |
| --- | --- | --- |
| Texto libre / `run` / `chat` en `interactive` | Actor por eventos, 19 tools, tarjetas, memoria | Journal y resumen de operacion en `.agentloop/` |
| `run`, `goal`, `lobos` independientes | Actor por eventos, archivos y procesos | Journal en `.agentloop/event-journal/` |
| `journal` | `GoalRunner` sobre el actor | Journal y snapshot/Journey en `artifacts/journeys/` |
| `chat` independiente | Una llamada sin herramientas | Consola |
| `run-chat` interactivo o independiente | Utilidad conversacional anterior con tools basicas, sin terminal | Hilo del chat interactivo; no acredita tarjetas |
| `discover` | Sondeo remoto de capacidades | Cache `artifacts/agentic-models.json` |
| `diagnostico`, `recuperacion` | Lectura de evidencia | Informe; no repiten efectos |

En el CLI: `modelos`, `modelo glm`, `modelo kimi` cambian el modelo de la
siguiente operacion. Durante texto libre/`run`: `estado`, `pausa`, `reanudar`,
`cancelar`, `aprobar` y `rechazar` publican controles independientes.

```powershell
dotnet run --project src/AgentLoop.Cli -- goal "Crea y lee un poema de cuatro versos sobre Mexico en poema.txt" --artifact poema.txt --contains Mexico
dotnet run --project src/AgentLoop.Cli -- goal "Crea y lee resultado.json" --artifact resultado.json --json
dotnet run --project src/AgentLoop.Cli -- recuperacion ".agentloop/event-journal/<runId>.jsonl"
```

Las herramientas se componen en `GoalRunFactory` y se filtran por `Scope`:

- Datos y archivos: `add_numbers`, `get_time`, `write_text_file`,
  `read_text_file`, `code_view`, `edit_text_file`, `list_directory`, `get_system_context`, `where_command`, `find_files`.
- Operaciones: `tarjeta`, `ejecutar`, `lanzar`, `esperar`, `leer`, `procesos`,
  `matar`, `observar`, `flujo`.

Para efectos: abrir tarjeta, ejecutar, observar un resultado exitoso, usar su
`receipt_id` como `evidencia_id` al comprobar y cerrar. Una respuesta textual
no crea archivos. Simular no produce evidencia de efectos reales.
Los criterios comprueban observacion, frescura, hash y contenido configurado;
la calidad literaria o correccion semantica requiere validadores adicionales.

## Seguridad de datos

[DTOs, sanitizacion y limites de confianza](docs/guides/seguridad-datos-harness.md).
Para archivos acotados: `interactive --no-shell --scope-root "C:\proyectos\demo"`.
El modo auto con shell conserva los permisos del usuario; no es un sandbox.

## Mapa del codigo

| Ruta | Responsabilidad |
| --- | --- |
| `src/AgentLoop.Kernel/` | Puertos LLM, DTOs neutrales, eventos y senales |
| `src/AgentLoop.Core/` | Tarjetas, contratos, estado, conocimiento y criterios |
| `src/AgentLoop.Harness/` | Actor por eventos, admision, contexto y puertos de efectos |
| `src/AgentLoop.Infrastructure/` | NVIDIA, disco, procesos, Docker y composicion de adaptadores |
| `src/AgentLoop.Cli/` | Comandos, consola interactiva y perfiles F5 |
| `src/AgentLoop.Compatibility/` | Chat y ejecutores anteriores |
| `tests/AgentLoop.SelfTests/` | Pruebas, sondas y fixtures |
| `examples/contracts/, examples/profiles/` | Ejemplos declarativos externos al motor |
| `docs/, scripts/` | Guias, informes, evidencia y auditorias |

El puerto activo es `ILLMClient`; los detalles NVIDIA viven en
[Provider/Nvidia](src/AgentLoop.Infrastructure/Providers/README.md). Sondas historicas requieren su revision de origen.

## Estado comprobado y documentacion

Arquitectura vigente y flujos ASCII: [harness por eventos](docs/architecture/harness-eventos.md).
Incidente y correccion POS: [informe](docs/incidents/pos-planeacion-sin-ejecucion-2026-10-01.md).
Validacion de recuperacion POS: [evidencia](docs/evidence/2026-10-01/README.md).
Validacion vigente de seguridad: [evidencia](docs/evidence/2026-10-01-seguridad/README.md).

Validacion local anterior del 1 de octubre: 382 comprobaciones correctas, 68 del
coordinador; compilacion con 0 errores y 0 advertencias. Sin nueva inferencia remota.

Validacion anterior registrada el 30 de septiembre de 2026, commit `8b20a3f`:
compilacion sin errores ni advertencias, 340 comprobaciones correctas y
prueba NVIDIA de poema con escritura, lectura, comprobacion y cierre.
No equivale a una comprobacion nueva de disponibilidad remota.

## Core, Kernel y proyectos genericos

[Arquitectura independiente y flujo ASCII](docs/architecture/core-kernel.md).
`AgentLoop.Kernel` expone puertos, contratos y senales; `AgentLoop.Core` conserva
estado y conocimiento; el ejecutable compone adaptadores y coordina por eventos.
El ejercicio POS fue archivado localmente y retirado de la implementacion.

```powershell
dotnet build AgentLoop.sln
$contract = Get-Content "examples/contracts/minimal.example.json" -Raw | ConvertFrom-Json
$contract.root = [IO.Path]::GetFullPath($contract.root)
New-Item .agentloop -ItemType Directory -Force | Out-Null
$contract | ConvertTo-Json -Depth 30 | Set-Content ".agentloop/minimal-contract.json" -Encoding utf8
dotnet run --project src/AgentLoop.Cli -- project-admit --project-contract ".agentloop/minimal-contract.json" --project-profile "examples/profiles/artifacts.example.json"
dotnet run --project src/AgentLoop.Cli -- project-run --project-contract ".agentloop/minimal-contract.json" --project-profile "examples/profiles/artifacts.example.json" --fixture --fixture-root "examples/fixtures/minimal"
```

[Guia de perfiles, contexto y recuperacion](docs/architecture/goals-proyectos.md).
No hay perfil implicito. El host registra validadores y comandos; las instrucciones
de dominio viven en perfiles externos. La raiz generada debe estar fuera del repo.
El ejemplo acredita archivos/JSON; para comportamiento se instala un validador externo.

[Compilacion de esta entrega](docs/evidence/2026-10-03-core-kernel/README.md).
Las 415 comprobaciones y el ejercicio aislado anteriores corresponden a 5132301.
Validacion posterior del Core/Kernel: [pruebas locales y NVIDIA con Docker](docs/evidence/2026-10-04-validacion-core-kernel/README.md).

[Indice documental](docs/README.md) Â·
[Arquitectura y pendientes](docs/architecture/implementacion-coordinador-eventos-2026-09-30.md) Â·
[Inventario y limpieza](docs/archive/limpieza-datos-repo-2026-09-30.md) Â·
[Indice de evidencia](docs/evidence/README.md)

Comprobaciones existentes: `dotnet run --project src/AgentLoop.Cli -- selftest` o `dotnet run --project src/AgentLoop.Cli -- selftest-coordinator`.
Inventario documental: `powershell -File scripts/audit-repository.ps1`.

## Configuracion segura

Usa `setup` (entrada oculta) y `setup --status` en la consola. Nunca pegues claves
en el chat ni en argumentos. [Wizard y limites](docs/guides/setup-credenciales.md).
[Pendientes agenticos priorizados](docs/plans/mejoras-agenticas-2026-10-07.md).

Arranque interactivo: comprueba credenciales locales y catalogo remoto (20 s);
`--offline` omite red. Consulta `credenciales verificar`.
[Errores y trazabilidad](docs/guides/resultados-observabilidad.md).

## Implementaci?n del 8 de octubre de 2026

[Flujo por eventos, pol?ticas, presupuestos, skills, grafos y edici?n recuperable](docs/guides/harness-planificacion-presupuestos.md).
