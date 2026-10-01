> Revision del 30 de septiembre. La evolucion de proveedores, contexto y
> recuperacion del 1 de octubre se documenta en [harness vigente](harness-eventos.md).

# Implementacion del coordinador por eventos

## Antes y despues

Base anterior: commit `e1690fb`. Rama de implementacion: `feat/harness-eventos-tarjetas`.
La [auditoria previa](auditoria-gaps-harness-2026-09-30.md) conserva el diagnostico
del estado anterior. Esta entrega sustituye el ejecutor principal de operaciones.

```text
ANTES
objetivo -> AgentLoopRunner -> StepAsync -> estado -> LLM
                                           ^          |
                                           |          v
                                     espera bus <- lote de tools
                                           |
                                  Aviso / Latido sintetico

IMPLEMENTADO
objetivo / proceso / resultado / control
                    |
                    v
             EventBus [run_id, event_id, causation_id]
                    |
                    v
             mailbox serial -> journal: received
                    |
              handler breve
                    |
        +-----------+----------------------+
        |                                  |
   hechos nuevos                      accion propuesta
        |                                  |
   solicitar LLM                    admision individual
        |                           [scope + tarjetas]
        v                                  |
 tarea independiente                journal: admitted
        |                                  |
 ModeloResultado                     tarea independiente
        |                                  |
        +----------> handler <--- HerramientaResultado
                          |
                  journal: resolved
                  resultado de tool + receipt_id
                          |
                  journal: applied
                          |
                   comprobar evidencia
                          |
                  cerrar -> terminal durable

consola -> pausa / reanudar / estado / cancelar / aprobar / rechazar
                            |
                         EventBus

observadores externos <- cola independiente
registro local de evidencia <- captura inmediata
```

`EventOperationCoordinator` no hereda de `StateMachine`, no llama `StepAsync`
y no usa `AgentLoopRunner`. Las continuaciones de su mailbox se crean al recibir
eventos. Las tareas de proveedor y herramientas publican resultados; sus esperas
no ocupan el handler. `ManualResetEvent` conserva la puerta de pausa y las senales
de procesos. El bucle de lectura de consola sigue siendo un transporte de entrada.

Texto libre y `run` en `interactive`, y los comandos independientes `goal`, `run`
y `journal`, ejecutan el nuevo coordinador. `chat`, `run-chat` y fixtures del
ejecutor anterior siguen disponibles como utilidades de compatibilidad.
Los comandos de utilidad ejecutados dentro del CLI conservan su propia entrada;
los controles concurrentes descritos aqui corresponden a texto libre y `run`.

## Contratos aplicados

- Cada accion se admite individualmente. Los efectos requieren una transaccion
  abierta y las acciones posteriores al cierre reciben rechazo con resultado de tool.
- Scope filtra modelos y herramientas y normaliza rutas respecto de `--cwd`.
  Una raiz permitida no admite directorios vecinos por prefijo. Con rutas restringidas
  se rechaza shell arbitrario y enlaces detectados; esto no constituye un sandbox.
- Herramientas exitosas de observacion producen recibos de la transaccion.
  `comprobar` exige `evidencia_id` real; `cerrar` exige comprobacion vigente.
- Los archivos esperados deben observarse, existir con contenido y conservar el
  hash comprobado. Por defecto deben cambiar respecto al inicio. Se pueden fijar
  contenido obligatorio y JSON valido. No se certifica automaticamente el sentido
  de una respuesta libre ni la calidad literaria de un poema.
- Deadline y cancelacion son eventos distintos. Se cancelan tareas cooperativas
  y procesos; hay hasta cinco segundos de drenado. Una accion que no responda
  queda marcada como incierta en el journal, para reconciliar antes de repetir.
- Simular no produce archivos ni recibos de efectos reales. Confirmar espera
  aprobacion por evento para escrituras tipadas y comandos de sistema.
- El registro JSONL se vacia a disco en cada entrada, antes de admitir efectos
  y despues de registrar resultados. La inspeccion informa pendientes y una
  escritura final incompleta; no reejecuta efectos automaticamente.
- Capacidad de procesos reservada atomicamente; snapshots de procesos sin cache;
  esperadores retirados al expirar/cancelar; regex limitada a 100 ms y 256 watchers.
- Escrituras de archivos y snapshots usan temporal y reemplazo. Archivos tipados
  tienen limite de 1 MB; el contexto aplica poda y rechaza contrato/parte actual
  que exceda el presupuesto de caracteres. No hay tokenizacion exacta ni resumen automatico.

## Uso

```powershell
dotnet run -- interactive
# Dentro: escribir un objetivo, luego estado / pausa / reanudar / cancelar.
# Con --politica confirmar: aprobar / rechazar habilita o rechaza el efecto pendiente.

dotnet run -- goal "Crea y lee un poema en poema.txt sobre Mexico" --artifact poema.txt --contains Mexico
dotnet run -- goal "Crea y lee resultado.json" --artifact resultado.json --json
# --allow-existing permite comprobar un archivo previo observado en esta operacion.

dotnet run -- recuperacion ".agentloop/event-journal/<runId>.jsonl"
dotnet run -- selftest-coordinator
dotnet run -- selftest
```

Los recibos, contratos y eventos de esta operacion son distintos de los antecedentes
y recuerdos persistentes de sesion. La memoria no acredita una tarjeta de cierre.

## Estado de los candidatos de la auditoria

| Candidato | Estado en esta entrega |
| --- | --- |
| C01 | Corregido en el camino principal: admision antes de cada efecto y rechazo tras cierre. |
| C02 | Parcial: recibos, frescura, hashes y criterios tipados; quedan validadores semanticos por dominio. |
| A01 | Corregido en operaciones principales: actor por eventos sin runner de pasos. |
| A02 | Corregido para control operativo: tareas pendientes, deadline, cancelacion y drenado. |
| A03 | Corregido en el actor: resultados registrados antes de observadores externos. |
| A04 | Parcial: admision central de modelos/tools/rutas; extensiones nuevas requieren clasificar politica de efectos. |
| A05 | Parcial: journal incremental e inspeccion; falta restauracion automatica reconciliada. |
| A06 | Corregido: reserva atomica del cupo de procesos. |
| A07 | Corregido en texto libre/run del CLI: productor de controles independiente. |
| A08 | Corregido: no hay sondeo periodico ni realimentacion Aviso/Latido; recuperacion acotada por causa. |
| M01 | Pendiente: capacidad y prioridad conjuntas del bus/mailbox/observadores. |
| M02 | Parcial: identidad causal y publicacion serial; falta normalizar todos los contadores heredados. |
| M03 | Corregido: retiro de esperadores cancelados y expirados. |
| M04 | Corregido: lectura de procesos sin cache de snapshots. |
| M05 | Parcial: regex, cupo y retiro al disparar/expirar espera; falta TTL general de watchers persistentes. |
| M06 | Parcial: presupuesto de contrato/parte/contexto y cap de archivos; faltan referencias y presupuesto por tokens. |
| B01 | Parcial: presupuestos del actor validados; persisten nombres de opciones heredadas. |
| B02 | Parcial: registro canonical de tools y factory compartida; quedan utilidades anteriores. |

## Evidencia

El directorio [evidence/2026-09-30](evidence/2026-09-30/) conserva compilacion,
suite completa, traza NVIDIA, poema y journal. Las pruebas sin red incluyen:
efectos antes/despues de tarjetas, evidencia inventada o alterada, criterios de
contenido/JSON, control durante espera del LLM, cancelacion de tools, aprobacion,
simulacion, observadores lentos, capacidad de procesos y espera sin sondeo.
Resultado final: compilacion con **0 errores y 0 advertencias**, suite completa
con **340 comprobaciones correctas**, incluidas **26 del coordinador**.

Prueba real NVIDIA `z-ai/glm-5.3`: **Succeeded**, 5 solicitudes al modelo,
5 tools, 40 eventos, 3 tarjetas sin rechazos y unos 100 segundos. El archivo
se escribio y leyo antes de comprobar y cerrar; la inspeccion del journal
no encontro acciones inciertas ni eventos recibidos sin confirmar.
Aunque se solicito `--thinking off`, el proveedor devolvio reasoning observado;
la traza registra ese comportamiento efectivo.
