# Reorganizacion implementada

## Base y Git

Base: `c196ffb`. Se preservaron los cambios locales de instrucciones y la propuesta.
`main` incorporo `feat/harness-eventos-tarjetas` mediante fast-forward; la implementacion vive en `feat/estructura-core-harness`.
No se eliminaron ramas ni se publicaron cambios. El ejercicio POS permanece externo.

## Antes y despues

```text
ANTES
agent loop/
|-- Program.cs                  comandos + sesion + pruebas (2644 lineas)
|-- AgentLoop.csproj             executable + implementaciones + pruebas
|-- Core/                       contratos/estado de proyectos
|-- Kernel/                     puertos y contratos agrupados
|-- Goals/, Cards/, Events/      reglas + efectos + orquestacion
|-- Provider/, Parsing/          NVIDIA y protocolo
|-- Harness/, Tools/, Terminals/ servicios + pruebas + utilidades
|-- StateMachines/, Looping/     contexto + persistencia + chat legado
|-- docs/                       guias, planes e informes mezclados
`-- examples/project-goals/

DESPUES
agent loop/
|-- AgentLoop.sln
|-- src/
|   |-- AgentLoop.Kernel/         contratos neutrales, senales y puertos
|   |-- AgentLoop.Core/           reglas, tarjetas, criterios y estado
|   |-- AgentLoop.Harness/        coordinador, contexto y puertos de efectos
|   |-- AgentLoop.Infrastructure/ disco, procesos, perfiles y proveedor
|   |   |-- Providers/
|   |   |   |-- Nvidia/           catalogo, controles y cliente
|   |   |   |-- Contracts/        transporte y serializacion de payload
|   |   |   |-- Transport/        HTTP, reintentos y errores
|   |   |   |-- Parsing/          respuesta y acumulacion SSE
|   |   |   `-- Discovery/        sondeos de capacidades
|   |   |-- Credentials/          carga desde almacenes dedicados
|   |   |-- Execution/            procesos, Docker y validadores
|   |   |-- Persistence/          journal, memoria y sesiones
|   |   `-- Composition/          instalacion de adaptadores
|   |-- AgentLoop.Compatibility/  chat y ejecutores anteriores
|   `-- AgentLoop.Cli/            Program minimo, Commands/, Interactive/, Properties/
|-- tests/AgentLoop.SelfTests/    ejecutable de verificacion y fixtures
|-- examples/contracts/          contratos de ejemplo
|-- docs/
|   |-- architecture/            arquitectura y matrices
|   |-- guides/                  operacion vigente
|   |-- plans/                   planes con revision de origen
|   |-- incidents/               causas, correcciones y limites
|   |-- archive/                 informes historicos
|   `-- evidence/                capturas con procedencia
|-- scripts/                     inventarios y auditorias
`-- local-secrets/               ignorado salvo README
```

Los inventarios completos estan en [antes](inventario-antes.txt), [despues](inventario-despues.txt) e [inventario por contenido](inventario-archivos.json).
La [matriz de los 139 fuentes originales](matriz-archivos.json) identifica destinos, tipos, naturaleza y accion.
El [mapa de rutas](migracion-rutas.json) permite localizar documentos y fuentes movidos.
La [propuesta inicial](estructura-propuesta.md) es el antes aprobado, no el indice del estado vigente.

## Nombre, contenido y responsabilidad

| Archivo anterior | Implementacion | Responsabilidad |
| --- | --- | --- |
| Program.cs | Program.cs + Program.<comando>.cs + BuiltInSelfTests.cs | Entrada minima, comandos agrupados, pruebas separadas |
| ResetEvents.cs | AsyncManualResetEvent.cs / AsyncAutoResetEvent.cs | Una primitiva identificable por archivo |
| ChatContracts.cs | ChatRequest.cs, ChatMessage.cs y tipos asociados | DTOs neutrales, sin atributos del proveedor |
| ThinkingOptions.cs | ThinkingOptions.cs + ChatPayloadSerializer.cs | Preferencia separada de serializacion wire |
| ProjectPorts.cs | Puertos, perfil y DockerExecutionDefinition.cs | Contrato neutral separado del adaptador concreto |
| BuiltInTools.cs | ArithmeticTools.cs, FileTools.cs, SystemTools.cs, ToolArguments.cs | Capacidades por naturaleza; BuiltInTools solo registra |
| GetWeather | CapabilityProbeTools.cs | Fixture de sondeo explicita; no herramienta de meteorologia real |
| Composition.cs | FunctionComposition.cs / ResiliencePolicyChain.cs | Composicion funcional frente a politicas de resiliencia |
| ConversationThread.cs | ConversationSession.cs | Historial de sesion, no maquina de estados |
| AgentContext.cs | ChatTurnContext.cs | Datos de la utilidad de chat de compatibilidad |
| ConversationMemory.cs | FileConversationMemory.cs + IConversationMemory.cs | Adaptador de notas y puerto neutral |
| ShellCommand.cs | ShellCommand.cs + ShellProcessStartInfoFactory.cs | Comando separado de lanzamiento del SO |
| ProjectScriptClient.cs | ProjectFixtureClient.cs en tests | Doble de prueba fuera de produccion |
| MachineState.cs | RunState.cs | Estado de ejecucion de un Run |
| AgenticModelSelector.cs | ModelCapabilityProbe.cs | Sondeos reales de capacidades del proveedor |
| EventDrivenAgent.cs | LegacyEventDrivenAgent.cs | Hace explicito que es un ejecutor anterior |

No se renombraron todos los namespaces publicos: se conservan para compatibilidad de consumidores.
Las carpetas y assemblies determinan las dependencias reales. Las partes del CLI conservan `Program` porque pertenecen al mismo host.
Los tipos anidados que son detalles privados permanecen con su propietario.

## Dependencias y puertos

```text
Cli -------------> Infrastructure -------> Harness -------> Core -------> Kernel
 |                         |                  |                            ^
 `-> Compatibility -------+------------------+----------------------------+
Tests -> assemblies bajo prueba
Produccion -X-> Tests
```

`CardLedger` recibe `IEvidenceManifest`; Core no lee archivos.
`AgentEvent` es un DTO; `AgentEventFormatting` aplica limites y sanitizacion al presentarlo.
`GoalContext` usa `IProcessSupervisor`; los handles y watchers tambien son puertos.
`GoalRunner` recibe `IGoalRunSetupFactory`; el host instala la fabrica concreta.
`OperationServices` instala parser, stream, clasificacion transitoria, journal y checkpoint.
`ProjectRuntime` y la validacion usan `IProjectResources`.
La sesion recibe `IConversationMemory` e `IConversationSessionStore`; persistencia sin adaptador falla cerrada.
Los constructores de contratos admitidos conservan admision restringida: Infrastructure es el assembly autorizado para construirlos.

El campo JSON historico `execution.docker` conserva compatibilidad de perfiles y hashes; es un `JsonElement` en el contrato neutral.
`DockerExecutionDefinition` y su validacion viven en Infrastructure. `settings` permite configuracion declarativa generica.
Los identificadores de comandos, validadores y limites siguen viniendo de perfiles externos.

## Flujo vigente

```text
entrada CLI / control / proceso / resultado / temporizador
                         |
                evento con identidad causal
                         |
              bus -> dispatcher -> handler breve
                         |
             admision + presupuesto + tarjetas
                   /                  \
          necesita decision       admite herramienta
                 |                      |
          solicitud asincrona      adaptador instalado
                 |                      |
          ModeloResultado       HerramientaResultado
                   \                  /
                    mismo bus y journal
                         |
          observacion host + recibo + hash + validacion
                         |
             actualizar estado / invalidar dependencias
                         |
             nueva accion habilitada o cierre acreditado
                         |
              checkpoint / Journey / evidencia
```

La puerta de pausa sigue siendo `AsyncManualResetEvent`. Las respuestas del modelo son hechos del bus.
Las iteraciones internas de protocolos y reintentos son detalles de transporte; no sustituyen el eje de eventos.
`ConversationSession` conserva antecedentes; no planifica las operaciones principales.

## Uso

```powershell
dotnet build AgentLoop.sln
dotnet run --project src/AgentLoop.Cli -- interactive
dotnet run --project src/AgentLoop.Cli -- selftest
python scripts/audit-architecture.py
powershell -File scripts/audit-repository.ps1
python scripts/audit-git-secrets.py
```

F5: proyecto de inicio `AgentLoop.Cli`, perfil `cli interactivo`. La solucion coloca ese ejecutable primero.
El perfil conserva argumentos y configura el directorio de trabajo en la raiz del repo.
La comprobacion realizada desde consola valida el arranque y los perfiles; no simula una pulsacion F5 en Visual Studio.
`selftest` descubre el ejecutable de tests por la propiedad `AgentLoopVerification`; `--verification-project` permite un proyecto instalado explicitamente.
Los fixtures de project-run se ejecutan desde tests y entregan un cliente inyectado al host.
No se agrego una dependencia de produccion al assembly de pruebas.

## Evidencia de la migracion

La compilacion, auditorias, suite y ejecucion NVIDIA se registran en `docs/evidence/2026-10-04-reorganizacion/`.
Los hashes y rutas dentro de capturas anteriores siguen representando su revision de origen.
Se detecto que el perfil historico code-profile.json fijaba hashes CRLF mientras el recurso versionado esta en LF;
el preflight rechazo la discrepancia. La nueva verificacion utiliza un perfil con hashes de los bytes actuales, sin cambiar la captura original.
