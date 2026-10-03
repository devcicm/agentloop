# Plan de implementacion: goals verificables para proyectos y POS

Fecha del plan: 2 de octubre de 2026. Actualizacion: 3 de octubre.
Estado: **P0-P5 implementadas; P6 con fixture aislado real y corrida NVIDIA registrada**.
Uso vigente: [goals de proyectos](goals-proyectos.md). Resultados y limites:
[evidencia del 3 de octubre](evidence/2026-10-03-proyectos/README.md).

Las secciones de diseno conservan los requisitos iniciales. La tabla siguiente
registra la implementacion; los nombres de clases se consolidaron en la proyeccion
del actor existente, sin otro motor de ejecucion.

| Fase | Implementacion actual |
| --- | --- |
| P0 | DTO estricto, admision y revision/hash |
| P1 | ProjectRuntime/ProjectState; eventos, DAG, tarjetas por intento y cierre integrado |
| P2 | Oraculos del host, resultados tipados, hashes frescos y evidencia consultable |
| P3 | Brief por tarea, estimador conservador, poda, presupuestos persistidos y progreso material |
| P4 | Backend Docker offline, perfiles fijos, aislamiento y limpieza confirmada |
| P5 | Journal autoritativo, snapshot/checkpoint y reconciliacion de efectos inciertos |
| P6 | Fixture POS real: compilacion, API, SQLite y navegador; NVIDIA evaluado por separado |

## 1. Resultado esperado

El harness debe construir un proyecto a partir de un contrato estable, registrar
sus entregables y cerrar el goal solo cuando validadores del host comprueben los
criterios obligatorios. Debe conservar evidencia, atender controles mientras
trabaja y recuperar su estado sin repetir efectos inciertos.

El POS sera el primer caso de integracion; el motor, los contratos y los
validadores generales deben servir tambien para otros proyectos.
Antecedentes: [incidente POS](incidentes/pos-planeacion-sin-ejecucion-2026-10-01.md),
[actor vigente](harness-eventos.md) y [seguridad](seguridad-datos-harness.md).

## 2. Estado observado y gaps

| Implementacion actual | Limitacion que aborda el plan |
| --- | --- |
| `EventOperationCoordinator` recibe eventos y despacha trabajos independientes | Falta estado de entregables con dependencias y validaciones funcionales |
| `CardLedger` mantiene una pila de transacciones anidadas | Una tarjeta de estado no equivale a un nodo de un grafo de trabajo |
| `OperationAcceptance` exige recibos, observacion y hashes vigentes | Existencia, contenido y JSON valido no demuestran comportamiento del POS |
| `AcceptanceCriteria` deriva extensiones documentales del objetivo | No constituye un manifiesto completo de proyecto |
| `OperationEventJournal` registra fases e inspecciona acciones pendientes | Inspeccionar no reconstruye automaticamente el estado ni reconcilia efectos |
| Contexto recortado y observaciones acotadas | Falta presupuesto de entrada por tokens y seleccion por tarea |
| Scope, schemas, recursos protegidos y sanitizacion | Shell AUTO conserva privilegios del usuario; las rutas acotadas bloquean shell |
| `RunnerOptions` tiene plazo y limite de solicitudes | 300 segundos no es un presupuesto validado para desarrollar un POS |

Un grafo de dependencias se agregara como estado del proyecto. Se conservaran
`abrir`, `avanzar`, `comprobar`, `cerrar` y `abortar` como protocolo transaccional.
En la primera version solo habra una transaccion de trabajo activa: la pila
actual no debe recibir cierres concurrentes de tareas independientes.

## 3. Alcance del caso POS

Perfil propuesto para la primera demostracion: aplicacion web local, TypeScript,
Node LTS disponible, SQLite y una sola caja. Es una propuesta de fixture, no una
decision universal para proyectos del usuario. Las versiones exactas se fijaran
al implementar, junto con el archivo de dependencias bloqueadas.

Incluye productos, precios, existencias, carrito, cobro en efectivo, cambio,
ticket con folio y persistencia. La venta y el descuento de inventario deben ser
una transaccion atomica. Usar centavos enteros para dinero y cantidades enteras
positivas en este perfil; nunca numeros de punto flotante para importes.

Quedan fuera del primer contrato facturacion fiscal, pagos bancarios, operacion
multisucursal, ventas offline sincronizadas y despliegue en produccion.

Datos de comprobacion sinteticos: producto de 2500 centavos, stock 10; venta de
2 unidades, pago de 10000 centavos, total 5000, cambio 5000 y stock final 8.
No se usaran datos reales de clientes ni credenciales de produccion.

## 4. Contratos y estructura propuesta

```text
Goals/
  Projects/
    Contracts/
      ProjectGoalDto.cs
      WorkItemDto.cs
      ArtifactSpecDto.cs
      ValidationSpecDto.cs
      ValidationResultDto.cs
      ProjectEventDataDto.cs
    ProjectContractAdmission.cs
    ProjectState.cs
    ProjectWorkScheduler.cs
    ProjectAcceptance.cs
    ProjectContextSelector.cs
  Validation/
    IProjectValidator.cs
    ProjectValidatorRegistry.cs
    ArtifactValidator.cs
    BuildValidator.cs
    ProcessProbeValidator.cs
  Recovery/
    ProjectStateRecovery.cs
    EffectReconciliation.cs
Harness/
  Execution/
    IProjectExecutionEnvironment.cs
    ProjectCommandAdmission.cs
    IsolatedProjectExecutionEnvironment.cs
  Context/
    ITokenBudgetEstimator.cs
    ProjectContextBudget.cs
  Durable/
    ProjectCheckpoint.cs
    ProjectEvidenceStore.cs
examples/
  project-goals/pos-minimo.example.json
docs/
  goals-proyectos.md
  evidence/<fecha>-goals-proyectos/
```

Nombres propuestos, no archivos existentes. Evitar carpetas adicionales si una
responsabilidad puede integrarse claramente en un componente vigente.

| DTO | Campos esenciales y autoridad |
| --- | --- |
| ProjectGoalDto | version, goalId, revision, objective, root, workItems, artifacts, validations, budgets, executionProfile; contrato admitido por el host |
| WorkItemDto | id, dependsOn, artifactIds, validationIds; estructura aciclica, ids unicos |
| ArtifactSpecDto | id, relativePath, required, expectedKind, changePolicy; rutas dentro de root |
| ValidationSpecDto | id, validatorId, workItemId, typedParameters, required, timeout; validatorId registrado por el host |
| ValidationResultDto | validationId, attemptId, contractHash, inputHashes, status, observations, evidenceRefs, startedAt, endedAt |
| ProjectEventDataDto | schemaVersion, workItemId, attemptId, actionId, contractHash y revision; identidad causal complementada por AgentEvent |

Validar con `StrictJson`, limites de tamano/profundidad, rechazo de campos
desconocidos, referencias inexistentes y ciclos. Ningun comando arbitrario de
validacion sera autorizado por aparecer en JSON generado por el modelo.
El operador define el contrato; el modelo puede proponerlo. La admision fija una
revision y su hash. Cambiar requisitos exige nueva revision e invalida resultados
afectados. Los resultados y recibos autoritativos los emite el host.

## 5. Flujos actual y propuesto

```text
ACTUAL
objetivo textual -> actor -> modelo -> admision -> herramienta
                      ^                             |
                      +------ evento resultado -----+
                                    |
                   recibo + artefacto observado
                                    |
               comprobar(hash/contenido) -> cerrar

PROPUESTO
objetivo + contrato versionado
              |
     validar y admitir contrato
              | evento: ProyectoAdmitido
              v
 journal -> mailbox -> estado de proyecto -> dependencias habilitadas
              |                                 |
              |                         seleccionar tarea concreta
              |                                 |
              |                         contexto bajo presupuesto
              |                                 |
              |                         modelo propone accion
              |                                 |
              |                         scope + admision + entorno
              |                                 |
              +<------- resultado de herramienta/proceso
              |
      artefactos observados -> validacion independiente del modelo
              |                         |
              +<---- ValidacionFinalizada / ValidacionFallida
              |
    aceptar tarea o habilitar correccion acotada
              |
    todas las tareas obligatorias aceptadas?
              |
    validacion integrada + evidencia vigente + procesos finalizados
              |
         cierre del goal

pausa / reanudar / cancelar / deadline -> mismo mailbox
```

Son nombres propuestos de eventos nuevos: `ProyectoAdmitido`, `TrabajoHabilitado`,
`ValidacionSolicitada`, `ValidacionFinalizada`, `ValidacionFallida`,
`TrabajoAceptado` y `ReconciliacionRequerida`. Se integraran en `AgentEventKind`
con payload tipado; no se creara un segundo bus para gobernar el proyecto.
`HerramientaResultado` y `ProcesoFin` existentes se reutilizaran.

El planificador calcula trabajo habilitado al aplicar un evento relevante.
No recorre tareas por reloj, no llama StepAsync y no consulta al modelo por
inactividad. Un timeout o backoff produce un evento programado y cancelable.
`AsyncManualResetEvent` conserva su papel de senal/puerta de pausa.

## 6. Estados, tarjetas y progreso

```text
Pendiente --dependencias aceptadas--> Habilitada
Habilitada --admision--> Activa --resultado--> PorValidar
PorValidar --validacion vigente OK--> Aceptada
PorValidar --defecto reproducible--> CorreccionPendiente --> Habilitada
Activa --efecto incierto--> RequiereReconciliacion
cualquier estado no terminal --cancelacion--> Cancelada
presupuesto agotado / error definitivo --> Fallida
```

Los estados pertenecen a la tarea; las tarjetas pertenecen a su transaccion.
`abrir` vincula workItemId y attemptId; `comprobar` exige los resultados de todas
sus validaciones obligatorias; `cerrar` acepta la tarea solo con evidencia
vigente. Los campos nuevos deben incorporarse al schema de `tarjeta` y a la
validacion del host. Abortar registra fallo: no revierte automaticamente archivos.

El goal exige ademas una transaccion final de integracion. Cerrar una tarea no
publicara `ObjetivoCumplido` si quedan otras pendientes. Revisar expresamente la
condicion terminal actual del actor y del ledger al implementar esta distincion.

Progreso material: cambio esperado de un artefacto, validacion que pasa o
dependencia satisfecha. Narracion, tool call propuesta y tarjeta `avanzar` no
reinician por si solas el limite de falta de progreso. Las correcciones tienen
presupuesto por tarea y global; nunca reintentos ilimitados.

## 7. Validacion y evidencia

Validadores registrados por el host, parametros tipados y ejecucion asincrona.
Para el fixture POS usar pruebas funcionales revisadas y mantenidas fuera del
directorio generado: el modelo no podra modificar el oraculo ni emitir su OK.
Las pruebas generadas por el proyecto pueden complementar esa comprobacion.

| Criterio obligatorio | Comprobacion |
| --- | --- |
| Proyecto instalable y construible | Dependencias bloqueadas, comandos autorizados y exit code 0 |
| Servicio disponible | Arranque, puerto asignado y respuesta esperada dentro de timeout |
| Productos persistentes | Alta, consulta y reinicio con datos conservados |
| Venta correcta | Total 5000, cambio 5000, stock 8 y ticket consistente |
| Atomicidad | Fallo inducido no deja venta parcial ni inventario descontado |
| Stock insuficiente | Rechazo sin cambios en ventas ni existencias |
| Doble envio | Misma clave de idempotencia no duplica venta ni descuento |
| Interfaz utilizable | Escenario de operador en navegador, comprobacion de controles y resultados |
| Entrega reproducible | README, configuracion de ejemplo y arranque en entorno limpio |

Un HTTP 200 o un exit code 0 aislado no acredita estos criterios. Cada resultado
incluye version del validador, contrato, hashes de entradas, intento, tiempos,
comandos admitidos, salidas acotadas y observaciones esperadas/reales.
Si cambia codigo/configuracion relevante, invalidar el resultado y los
dependientes. Hashes identifican contenido; no prueban autenticidad criptografica.

El servicio de prueba se detiene y se observa `ProcesoFin` antes del cierre.
Este plan mantiene la regla vigente de procesos finalizados; entregar un servicio
permanentemente vivo requeriria otro contrato de transferencia de responsabilidad.

## 8. Contexto, memoria y presupuesto

Conservar fuera del prompt el journal completo. Seleccionar contrato vigente,
tarea activa, dependencias aceptadas, mapa de archivos y ultimos defectos con
referencias a evidencia. Obtener contenido adicional por herramientas de lectura
acotada. Memoria personal y outputs externos siguen siendo datos no confiables.

Calcular presupuesto antes de cada solicitud incluyendo sistema, mensajes,
schemas de tools, reserva de salida y margen. El estimador declara modelo,
version y si la cifra es exacta o conservadora; no llamar tokens reales a una
aproximacion por caracteres. Si no cabe el contrato minimo, detener con un
diagnostico concreto. No eliminar requisitos silenciosamente.

Configurar explicitamente deadline global, plazo por validacion, solicitudes,
tokens/coste si disponible, intentos de correccion y procesos concurrentes.
Los valores se calibraran con evidencia local y luego remota; ampliar 300 segundos
sin medir no soluciona el incidente. El transporte NVIDIA permanece independiente.

## 9. Entorno de ejecucion y recuperacion

Resolver antes de ejecutar proyectos el conflicto actual: `--scope-root` bloquea
shell. Crear una capacidad separada de comandos de proyecto autorizados por
perfil: ejecutable resuelto, argumentos estructurados, cwd permitido, timeout,
entorno minimo y politica de red. Invocar sin interpolacion de shell.
Una allowlist no contiene scripts de dependencias: ejecutarlos en un entorno
aislado, con solo la raiz de proyecto montada y sin claves ni memoria del host.

Para Windows se propone un backend de contenedor compatible disponible; su
disponibilidad sera comprobada en preflight. No asumir Docker instalado.
Sin backend, el perfil aislado falla antes de efectos. El modo local heredado
puede conservarse con sus limites declarados, pero no acredita aislamiento.
Instalacion: red limitada segun perfil; escenarios funcionales: red externa
deshabilitada. Limitar CPU, memoria, disco y vida de procesos del entorno.

```text
reinicio -> leer journal valido + checkpoint compatible
                        |
             reconstruir estado sin efectos
                        |
             acciones admitidas sin resolver?
                  |                 |
                 no                si
                  |                 v
                  |       inspeccionar artefactos / procesos
                  |                 |
                  |       resuelto / no aplicado / incierto
                  |                 |
                  |       registrar reconciliacion autorizada
                  +-----------------+
                        |
             revalidar evidencia -> habilitar trabajo
```

EventId permite deduplicar aplicaciones; actionId e intento vinculan efectos.
No prometer exactamente una ejecucion de shell. Un efecto incierto bloquea su
tarea hasta reconciliacion; escribir un snapshot no elimina esta ventana.
El checkpoint es derivado, versionado y reemplazable atomicamente. El journal es
la fuente de reconstruccion. Validar secuencias, hash de contrato y payloads;
una linea final truncada se diagnostica, corrupcion intermedia detiene recuperacion.
Continuar en un segmento nuevo vinculado al anterior: no sobrescribir el journal
que hoy se crea mediante FileMode.CreateNew.

## 10. Fases de implementacion y condiciones de salida

| Fase | Trabajo e integracion | Condicion de salida |
| --- | --- | --- |
| P0: contrato | DTOs, admision, ejemplo POS, CLI propuesto `--project-contract`, GoalRunFactory/RunnerOptions | Contrato invalido falla antes de tools; revision registrada; goals simples conservan comportamiento |
| P1: entregables por eventos | ProjectState/Scheduler, eventos, asociacion de tarjetas y gate terminal en actor | Dependencias correctas; una tarea cerrada no termina el goal; controles siguen disponibles |
| P2: aceptacion funcional | Registry, ejecutor async, ProjectAcceptance y EvidenceStore | Cierre rechazado con validacion faltante/fallida/obsoleta; texto del modelo no acredita OK |
| P3: contexto y progreso | Selector, estimador y presupuestos por tarea/globales | Solicitudes dentro del presupuesto; lectura recuperable; narracion no cuenta como progreso |
| P4: ejecucion de proyecto | CommandAdmission, backend aislado, preflight y limpieza | Build/servicio ejecutables dentro del perfil; ausencia de backend produce error explicito |
| P5: recuperacion | Checkpoints, replay sin efectos, reconciliacion y segmentos | Crash en ventanas de admision/efecto/resultado no duplica accion incierta |
| P6: POS integrado | Fixture, oraculos funcionales externos y corrida completa | Todos los criterios de seccion 7 pasan; evidencia reproducible y servicio detenido |

Orden inicial: P0 -> P1 -> P2 -> P3 -> P4 -> P5 -> P6. Los validadores de P2
se ejercitan primero con fixtures y ejecutores simulados; los comandos reales
del proyecto dependen de P4. La prueba integrada remota depende de todas las fases.

Cambios principales sobre archivos existentes: `EventOperationCoordinator`,
`OperationAcceptance`, `OperationAdmission`, `GoalContext`, `AgentEvent`,
`CardLedger`, schema de `OperationTools`, `GoalRunFactory`, `GoalRunner`,
`RunnerOptions`, `Program` y journal. Mantener adaptador para goals textuales y
`--artifact`; activar el nuevo contrato explicitamente durante la migracion.
No migrar ni reescribir journals historicos. Versiones desconocidas se rechazan.

## 11. Plan de pruebas y documentacion de cada fase

Pruebas previstas, **no ejecutadas en esta entrega documental**:

- Contratos: duplicados, ciclos, rutas con espacios/fuera de raiz, ids invalidos,
  schema desconocido, presupuestos imposibles y revisiones de requisitos.
- Eventos: duplicados, resultados tardios de intentos anteriores, pausa durante
  validacion, cancelacion, deadline, procesos silenciosos y dependencia fallida.
- Aceptacion: recibo falsificado, salida con instrucciones, validatorId desconocido,
  OK generado por el modelo, archivo cambiado despues de validar y criterios vacios.
- Contexto: contrato protegido durante poda, schemas grandes, estimador no exacto,
  lectura acotada y limite de progreso material.
- Ejecucion: argumentos con espacios, falta de backend, limpieza tras fallo,
  limites de recursos, acceso a secretos y red conforme al perfil.
- Recuperacion: caida antes/despues de admision, efecto y resultado; journal
  truncado/corrupto; checkpoint incompatible; ninguna repeticion automatica incierta.
- Integracion: POS local sin modelo con acciones deterministas; despues NVIDIA
  con presupuesto explicito. Registrar modelo y resultados, sin publicar la clave.

Cada fase entrega codigo, guia vigente, ASCII actualizado, pruebas locales
correspondientes y evidencia fechada con procedencia. La corrida remota no
sustituye pruebas deterministas. El numero historico de 382 checks corresponde
a la entrega anterior; no acredita funcionalidades propuestas aqui.

## 12. Resultado de implementacion

P0-P6 se implementaron sobre el mismo coordinador. El fixture aislado completo
paso; el resultado remoto y sus defectos se registran por separado. No se utiliza
la salida del modelo como prueba de exito ni se copian archivos del fixture sobre
el proyecto remoto para aparentar que el modelo lo completo.

Desviaciones explicitas: estimador UTF-8 conservador, sin tokenizer exacto;
checkpoint derivado sin lectura optimizada; dependencias offline limitadas a la
imagen preparada; interfaz POS de un producto por venta, API con varios items.
La garantia de cierre se limita a los criterios y oraculos versionados.
