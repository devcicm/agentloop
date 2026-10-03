# Goals de proyectos: ejecucion por eventos

Actualizado: 3 de octubre de 2026. P0-P5 implementadas; P6 tiene fixture
integrado real y pruebas remotas registradas en la [evidencia](evidence/2026-10-03-proyectos/README.md).
El resultado del modelo remoto se informa por separado del fixture.

## Preparacion y comandos

.NET 9 y Docker con motor Linux. Preparar la imagen local una vez desde la raiz:

```powershell
docker build -t agentloop-project-node:1 -f ProjectAssets/Dockerfile ProjectAssets
dotnet run -- project-admit --project-contract "examples/project-goals/pos-minimo.example.json"
dotnet run -- project-check-backend --project-contract "examples/project-goals/pos-minimo.example.json"
dotnet run -- project-run --project-contract "examples/project-goals/pos-minimo.example.json" --fixture
```

`project-admit` valida sin API key, sin procesos y sin crear root. Devuelve el
contrato normalizado y SHA-256; admitir no acredita entregables. `project-check-backend`
requiere root existente y verifica montajes, usuario, red y limites reales.
`--fixture` selecciona un cliente determinista que escribe el ejemplo versionado:
no consulta NVIDIA. Ejecuta el mismo actor, tarjetas, herramientas y validadores reales.

Para construir con NVIDIA, usar un contrato con root propio y ejecutar:

```powershell
dotnet run -- project-run --project-contract "mi contrato.json" --model z-ai/glm-5.3
```

La clave se obtiene del almacen dedicado, sin incorporarla al contrato. Tambien
se admite `moonshotai/kimi-k3`. El backend se comprueba antes de crear el cliente
remoto. No se cambia al ejecutor textual si falla. `goal`, `run` y `journal` con
`--project-contract` utilizan esta ruta. Desde el CLI interactivo se puede escribir
`project-run ...`; los controles existentes pertenecen al mismo supervisor.

## Antes y flujo actual

```text
ANTES (P0, 81455f5)
contrato -> admision/hash -> bloqueo de ejecucion
objetivo textual -> tarjetas generales -> evidencia documental
                                      (sin oraculo funcional POS)

ACTUAL
contrato -> DTO estricto -> revision/hash -> preflight aislado
                                            |
                                EventOperationCoordinator
                                mailbox + EventBus existentes
                                            |
                             ProyectoAdmitido / TrabajoHabilitado
                                            |
                         LLM recibe tarea, defectos y presupuesto
                                            |
                      tarjeta abrir [work_item_id + attempt_id]
                                            |
                       herramientas acotadas -> artefactos/hash
                                            |
                       project_validate -> trabajo asincrono
                         |                  |
              puerta manual compartida     Docker/oraculo
                         |                  |
                         +---- ValidacionFinalizada/Fallida
                                            |
                      comprobar [recibo real] -> cerrar tarea
                                            |
                       TrabajoAceptado -> habilitar dependencia
                                            |
                          todas las tareas aceptadas
                                            |
                      validar TODO [__goal__, integration]
                                            |
                       comprobar raiz -> cerrar -> objetivo cumplido

pausa -> Reset de AsyncManualResetEvent -> no nuevo trabajo
reanudar -> Set -> trabajo habilitado continua
cancelar/deadline -> cancelar worker -> retirar contenedor -> drenar
```

Las solicitudes al modelo reaccionan a eventos; no se agrega un motor de turnos,
`StepAsync`, temporizador de sondeo ni un segundo bus. Recorrer un DAG o un batch
finito de validaciones no conduce el ciclo de vida de la operacion.

## Componentes y contratos

| Componente | Responsabilidad |
| --- | --- |
| ProjectContractAdmission / ProjectGoalDto | JSON estricto, campos requeridos, referencias, rutas, DAG y presupuestos |
| ProjectRuntime / ProjectState | Proyeccion del actor, una tarea activa, dependencias, intentos, tarjetas y cierre |
| ProjectExecutionDtos | Estado, resultados tipados, hashes y snapshot recuperable |
| ProjectValidationExecutor | Validadores del host, entradas antes/despues y evidencia privada |
| DockerProjectEnvironment | Perfiles fijos, preflight, procesos y limpieza confirmada |
| ProjectContextBudget | Estimacion conservadora y poda de intercambios completos |
| ProjectRecovery | Journal, checkpoint derivado y reconciliacion explicita |
| ProjectScriptClient | Fixture determinista declarado como tal |

Version de contrato 1; revision independiente. El ejemplo revision 2 declara
cuatro tareas, diez archivos y validadores `artifact`, `build`, `process-probe`,
`pos-functional`. Solo se escriben archivos declarados. La raiz excluye recursos
protegidos y enlaces; dentro del repo solo se permite `artifacts/generated/<proyecto>`.
No puede contener el propio harness. Se conserva una baseline para `must-change`.
Modificar entradas invalida evidencia y tareas dependientes; un texto del modelo
no reemplaza el resultado emitido por el host.

El host abre la tarjeta raiz. Cada tarjeta de tarea identifica trabajo e intento;
no se permiten tareas anidadas adicionales. Cerrar un hijo no completa el goal.
La raiz exige revalidacion integrada, hashes actuales, tareas aceptadas y ningun
proceso activo. `project_validate` y `project_command` exigen la transaccion correcta.
`project_evidence` consulta un UUID de evidencia registrado por el host: no acepta
rutas arbitrarias. Los defectos y salidas externas se marcan como datos sin confianza.

## Validacion, contexto y ejecucion

Los resultados incluyen contrato, intento, entradas SHA-256, version del validador,
ambiente, tiempos y evidenceId. Se guardan en `.agentloop/project-evidence/<runid>/`;
los snapshots acotan observaciones a 512 caracteres; la herramienta consulta el registro
con hasta 4000 caracteres, conservando un limite de datos.
Un batch fallido consume correcciones. Acciones sin cambio material no cuentan como
progreso; los limites de llamadas, tiempo, salida y correcciones siguen vigentes.

La estimacion de contexto usa bytes UTF-8 del request mas margen; **no es tokenizer
exacto**. Preserva sistema y brief actual, elimina intercambios completos antiguos
y rechaza un minimo imposible. Recuperar conserva deadline, llamadas, correcciones
y baseline; no renueva presupuestos automaticamente.

Los comandos del modelo se limitan a perfiles `install` y `build`. Los validadores
usan perfiles del host. Docker recibe argumentos separados, imagen fijada por ID,
proyecto y oraculos de solo lectura, usuario no root, red externa deshabilitada,
capabilities retiradas, limites de memoria/CPU/PIDs y tmpfs. El proyecto se copia
a almacenamiento efimero interno; pruebas y SQLite no escriben sobre el proyecto
host. No se transmite el entorno de credenciales. La preparacion de imagen puede
usar red; la ejecucion es offline y no instala dependencias arbitrarias ausentes.

El oraculo compila todos los TS independientemente del script declarado y prueba
API, ventas, cambio, stock, atomicidad de rechazos, idempotencia, persistencia tras
reinicio y operacion de caja en navegador Playwright. Es un POS minimo; la interfaz
maneja un producto por venta y no incluye fiscalidad, autenticacion ni despliegue.
No acredita cualquier proyecto ni seguridad exhaustiva de codigo generado.
Las restricciones de contenedores siguen las [opciones oficiales de Docker](https://docs.docker.com/engine/containers/run/).
Los goals generales mantienen su politica anterior: este aislamiento es del perfil
`isolated-project-v1`.

## Recuperacion

```powershell
dotnet run -- project-inspect --project-contract "mi contrato.json" --resume-journal ".agentloop/event-journal/<id>.jsonl"
dotnet run -- project-reconcile --project-contract "mi contrato.json" --resume-journal "<journal>" --action-id "<id>" --decision applied --note "Estado observado y comprobado"
dotnet run -- project-run --project-contract "mi contrato.json" --resume-journal "<journal>"
```

Un efecto admitido sin resultado durable bloquea su repeticion. El operador verifica
archivos y contenedores, y registra `applied` o `not-applied`; una decision no prueba
por si sola la funcionalidad. El sidecar vincula hash del journal, contrato, accion
y archivos observados; cambios posteriores requieren revisar la procedencia.
Se rechaza corrupcion intermedia y se conserva lo anterior a una ultima linea
truncada. El journal es autoritativo; el checkpoint atomico es una proyeccion
derivada, sin optimizacion de lectura aun. Recuperar no repite herramientas previas:
restaura tareas aceptadas vigentes y revalida antes de cierre. Contenedores previos
vivos o estado no comprobable impiden continuar.

## Evidencia y alcance

Ejecutar `dotnet run -- selftest-projects` para los checks de mecanismos con backend
simulado; `selftest` tambien los incluye. No confundir esos checks con compilacion,
API o navegador reales. Consultar [resultados y procedencia](evidence/2026-10-03-proyectos/README.md)
para el fixture aislado y la corrida NVIDIA. Presupuestos agotados, errores del
proveedor o defectos del proyecto se conservan como resultados fallidos.

## Resultado remoto y mejora pendiente

El fixture aislado completa el contrato. GLM genero archivos, pero fallo en
compilacion y luego agoto el limite de falta de progreso al reanudar. Los dos
resultados estan en la evidencia: no se acredita un POS remoto funcional.
Queda un candidato de contexto de prioridad alta: conservar referencias de
lecturas y defectos durante la poda para reducir relecturas sin correcciones.
La herramienta de evidencia ya existe; la prueba remota no la utilizo.
