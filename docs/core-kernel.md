# Kernel, Core y host del harness

Actualizado: 3 de octubre de 2026. Sustituye el acoplamiento al ejercicio de la
revision 5132301. El POS se retiro del codigo y de los assets desplegados por el
proyecto. Sus fuentes se conservaron en `artifacts/exercises/pos-legacy/`, ignorado
por Git. La evidencia fechada conserva su valor historico para aquella revision.

## Dependencias y estructura

```text
AgentLoop (host ejecutable / CLI / composicion)
    |                   |
    v                   v
AgentLoop.Core ------> AgentLoop.Kernel
    |
    +-- contratos de proyecto y snapshots
    +-- estado de tareas / dependencias / intentos
    +-- conocimiento: lecturas, hashes, notas y presupuestos
    +-- puerto de validadores y factories

Kernel/
+-- LLM/           ILLMClient, mensajes y configuracion de razonamiento
+-- Events/        vocabulario de eventos, IDs numericos conservados
+-- Concurrency/   senales de reset manual y automatico
+-- Projects/      puertos de ejecucion, perfil y politicas

Host/
  Cli/                     comandos de proyectos
  Goals/                   actor y proyeccion de ejecucion
  Harness/Composition/     servicios y registro de plugins
  Harness/Execution/       adaptadores y validadores instalados
  Provider/                adaptadores y transporte por proveedor
```

Core y Kernel son bibliotecas .NET 9 independientes; Kernel no referencia Core,
y ninguna referencia el ejecutable, Docker, NVIDIA, SecretStore o archivos del
POS. Se conservan los namespaces publicos anteriores para compatibilidad del
codigo fuente consumidor; los consumidores binarios deben recompilarse. La composicion del host inyecta `ProjectServices`; el runtime
no construye Docker ni busca un ejercicio automaticamente.

## Perfil externo y admision

El contrato describe objetivo, archivos, dependencias y validaciones requeridas.
El perfil instalado por el host describe capacidades: instrucciones, validators,
commands, execution y policy. Se requiere `--project-profile` para ejecutar.

```text
contrato -> admision estructural -> hash
perfil externo del host -> DTO estricto -> capacidades / parametros / hash
                    \          /
                     Bind antes del LLM
                              |
                     adaptador.Preflight
                              |
                    actor existente + bus
                              |
                  tarea e intento habilitados
                              |
           puerto LLM / archivos / validador registrado
                              |
                   resultado tipado como evento
                              |
             actualizar Core y evidencia del host
                              |
             dependencias / integracion / cierre
```

Los IDs de perfil, comandos, validadores y escenarios se resuelven por registro.
El loader acepta registros adicionales de adaptadores y factories de validadores;
`execution.settings` permite configuracion de plugins. Los adaptadores instalados
por defecto son `artifact` y `docker`; los handlers son `artifact` y `command`.
Un contrato del agente no puede instalar nuevos adaptadores, modificar el perfil
ni aportar comandos de shell para que el host los ejecute.

`IProjectEnvironmentFactory` y `IProjectExecutionEnvironment` viven en Kernel.
`IProjectValidatorFactory` y `IProjectValidator` viven en Core. La factory valida
la definicion antes de pedir trabajo al modelo. Un plugin puede implementar esos
puertos y registrarse en `ProjectProfileLoader.Load` o componer `ProjectServices`.

## Contexto y progreso

```text
leer archivo -> artifactId + hash + numero de lecturas
                         |
        project_note [artifact_id, hash, note acotada]
                         |
              conocimiento persistido en snapshot
                         |
          brief actual conserva referencias y hallazgos
                         |
      cambio de hash -> nota marcada como desactualizada

lectura nueva -> presupuesto de exploracion acotado
relectura sin cambio / nota / narracion -> no acredita progreso material
archivo cambiado / nueva validacion correcta / tarea aceptada -> progreso
```

Las notas son datos del modelo etiquetados sin confianza; no crean evidencia ni
renuevan presupuestos de progreso. El brief incluye hashes actuales y lecturas
previas aunque se poden intercambios del chat. `project_evidence` entrega paginas
con offset y next_offset. Los limites de exploracion, falta de progreso, notas,
observaciones, journal, bytes de archivos y salida se toman del perfil. Deadline,
llamadas y correcciones provienen del contrato. La recuperacion conserva contexto
y contadores. Los hashes de contrato, perfil e inputs forman la vigencia de una
validacion; cambiar el perfil bloquea la reutilizacion de resultados anteriores.

La poda del proyecto usa su presupuesto, actualiza el historial vivo y preserva
el conocimiento fuera de los mensajes. Un fallo de integracion invalida sus
entradas y habilita correccion por dependencias, consumiendo presupuesto del
propietario de la validacion.

El estimador UTF-8 sigue siendo conservador; el puerto `ITokenBudgetEstimator`
permite sustituirlo. Esta entrega no incluye tokenizer especifico ni lectura
optimizada desde checkpoint.

## Seguridad de adaptadores

Las instrucciones y scripts del perfil pertenecen al host. La raiz generada debe
ser independiente del repo del harness y no contener perfil, evidencia o assets.
Docker recibe imagen, executable, entryPoint, mounts, usuario, limites, entorno
permitido y comandos desde el descriptor. Fija la imagen por ID, monta recursos
readonly y verifica `resourceHashes` antes de ejecutar. Los comandos del modelo
exigen `modelCallable=true`. El adaptador mantiene red externa deshabilitada,
capabilities retiradas y no-new-privileges. Confirma limpieza tras cancelacion.

La comprobacion de hashes depende de recursos del host bajo control del operador;
no constituye firma criptografica de autor. Los validadores externos definen el
criterio funcional. Un perfil de presencia/JSON no certifica una aplicacion.

## Uso y compatibilidad

Ver [guia de proyectos](goals-proyectos.md). El modelo lo selecciona el host;
GoalRunner requiere `Run.Model` o `RunnerOptions.Model`. El coordinador no decide
esfuerzos GLM: esa adaptacion pertenece al proveedor. Los comandos de proyecto se
separaron en Cli/ProjectCommands.cs. Los journals anteriores pueden inspeccionarse;
su evidencia sin profileHash no se reutiliza para ejecutar bajo el nuevo perfil.

Compilacion registrada en [evidencia de esta entrega](evidence/2026-10-03-core-kernel/README.md).
No se ejecutaron nuevas pruebas funcionales ni inferencias remotas en esta entrega;
las 415 comprobaciones y el ejercicio POS corresponden a 5132301.
