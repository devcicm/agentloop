# Harness vigente: arquitectura e implementacion

Actualizado: 1 de octubre de 2026. Base del actor: commit 8b20a3f;
esta entrega organiza proveedores y corrige contexto/recuperacion del bloqueo POS.
El [informe del incidente](incidentes/pos-planeacion-sin-ejecucion-2026-10-01.md)
contiene el antes y sus limites. La [revision del 30](implementacion-coordinador-eventos-2026-09-30.md)
conserva la implementacion y validacion anteriores.

## Componentes y responsabilidades

| Componente | Responsabilidad implementada |
| --- | --- |
| Program / CLI interactivo | Entrada, modelo, preferencias y controles independientes |
| GoalRunFactory | Bus causal, scope, supervisor, registro de tools y tarjetas |
| OperationContextBuilder | Requisitos de seguimiento y plantilla acotados, sin JSON del hilo completo |
| EventBus | Publica eventos identificados y sus causas |
| EventOperationCoordinator | Actor de operacion; mailbox serial, handlers breves, trabajos independientes |
| OperationAdmission / Scope | Valida cada accion, ruta, herramienta y estado transaccional |
| CardLedger / OperationAcceptance | Apertura, recibos reales, comprobacion, hashes y cierre |
| ProcessSupervisor | Procesos, salida, fin, observadores y capacidad concurrente |
| AsyncManualResetEvent | Puertas asincronas de pausa/senal; no constituye un motor de polling |
| ILLMClient / NvidiaClient | Contrato independiente del proveedor; adaptacion GLM y HTTP/SSE |
| OperationEventJournal / Journey | Evidencia durable, correlacion y diagnostico sin repetir efectos |

## Flujo anterior y flujo vigente

```text
EJECUTOR ANTERIOR (compatibilidad)
entrada -> runner -> StepAsync -> estado -> modelo -> lote tools
                       ^                              |
                       +------ espera / latido -------+

ACTOR PRINCIPAL VIGENTE
objetivo / salida de proceso / resultado / control
                         |
                         v
 EventBus [run_id, event_id, causation_id]
                         |
                         v
 mailbox serial -> journal received -> handler -> journal applied
                         |
          +--------------+--------------------+
          |                                   |
 hechos requieren decision              accion propuesta
          |                                   |
 contexto breve + observaciones         admision por accion
          |                                   |
 ILLMClient -> adaptador NVIDIA          tarjeta/scope/politica
          |                                   |
 trabajo async -> resultado evento      trabajo async -> resultado evento
          |                                   |
          +---------------+-------------------+
                          |
                 journal resolved + recibo
                          |
          comprobar -> criterio/hash -> cerrar
                          |
                    journal terminal

pausa/reanudar/cancelar/aprobacion/deadline -> EventBus -> handler
             (se atienden mientras el modelo o una tool espera)
```

Una llamada al LLM es una reaccion habilitada por hechos pendientes. Un contador
de llamadas limita recursos; no impulsa el trabajo. Procesos silenciosos no
generan consultas periodicas. Recuperacion y deadline son eventos programados
una vez; no hay un latido permanente. El framing de transporte y la cola de
mensajes pueden iterar datos; el eje de operaciones es la publicacion de eventos.

## Contexto y memoria de operacion

```text
hilo de sesion + instruccion actual
                 |
     seguimiento reconocido? -- no -> objetivo independiente
                 |
                si
     ultimos 4 requisitos de usuario (1500 chars cada uno)
                 |
     contrato fijo + instruccion actual prevaleciente
                 |
     plantilla/memoria de sesion <= 6000 chars
                 |
     hechos + tarjetas + procesos + indice ultimas 6 observaciones
                 |
     poda por segmentos completos, presupuesto de historial
                 |
     solicitud al modelo; historial completo permanece fuera del prompt
```

El builder se aplica a operaciones interactivas. Goal/journal independientes
reciben sus propios objetivos y contratos. El hilo no se serializa entero como
system: no se heredan pensamientos, ASCII de asistentes ni dumps de herramientas.
El objetivo efectivo queda registrado como taskContext en la evidencia de la
operacion interactiva. La herencia usa una heuristica explicita de aprobacion o
continuacion; no es un planificador semantico. Requisitos muy largos se recortan
con aviso: dividirlos o consultar la sesion guardada. Las observaciones recientes
conservan recibos aunque se pode el historial; los resultados completos se leen
en el journal. El indice acotado no sustituye validacion de recibos ni archivos.

## Recuperacion y cierre

```text
respuesta sin tools, sin procesos activos
                 |
 contador sin progreso -> limite alcanzado -> Failed + motivo
                 |
 aun hay presupuesto
                 |
 evento de recuperacion unico
                 |
 tool_choice=required + accion pequena + una escritura por llamada
 GLM reasoning_effort=low; respuesta truncada: max_tokens acotado a 8192
                 |
 tool admitida -> resultado registrado -> nueva decision por evento
                 |
 comprobar(receipt_id real) -> cierre validado o rechazo recuperable
```

El presupuesto de recuperacion duplica el configurado una vez, hasta 8192;
no escala indefinidamente 2048->4096->8192 tras cada truncamiento. El actor no
reenvia razonamiento de respuestas previas; el journal conserva el original.
Una propuesta de tool reinicia el contador de narracion: no prueba por si sola
que exista progreso material, y los rechazos siguen consumiendo tiempo/llamadas.
La admision del modelo registra max_tokens, effort, recovery, tool_choice y
medida de historial. Los ajustes finales por proveedor se prueban en su adaptador.

Para efectos: abrir -> ejecutar/escribir -> observar -> comprobar con recibo
-> cerrar. Simulacion, texto de plan o reasoning no acreditan una implementacion.
Un cierre requiere procesos terminados, evidencia vigente y los criterios
configurados. La frescura/hash/contenido/JSON no garantiza calidad funcional de
un proyecto: usar --artifact/--contains/--json y comprobaciones del servicio.
La derivacion automatica actual reconoce solo extensiones documentales; no
genera un manifiesto completo de HTML/JS/CSS ni valida semanticamente un POS.

## Entradas, persistencia y limites

Texto libre/run interactivo y goal/run/lobos usan el actor. Journal usa GoalRunner
sobre el actor. Chat/run-chat son utilidades conversacionales anteriores y no
acreditan el contrato de operacion. StateMachines/Looping se conservan para ellas
y para fixtures; su existencia no gobierna el actor principal.

Journal JSONL: received, applied, admitted, resolved y terminal. La inspeccion
identifica eventos sin confirmar y acciones admitidas sin resultado; no reejecuta
acciones inciertas automaticamente. Operation JSON conserva resumen, mensajes,
uso y tarjetas; Journey captura resultados antes de terminal. El journal local
puede incluir contenido y razonamiento: publicar solo extractos revisados.

Pendientes: presupuesto por tokens reales de entrada, manifiesto de proyecto
acordado antes de ejecutar, validadores funcionales por dominio, reconciliacion
manual de efectos no cooperativos y sondeo remoto del dialecto Kimi K3.
No se garantiza un POS terminado en el deadline predeterminado de 300 segundos.

## Seguridad de datos y DTOs

Los resultados externos se etiquetan, normalizan y validan. Las acciones pasan
por schema y recursos protegidos ademas de tarjetas/scope. La implementacion y
los limites de --no-shell/--scope-root se explican en [seguridad de datos](seguridad-datos-harness.md).

## Validacion

Compilacion y selftest sin red: ver [evidencia de esta entrega](evidence/2026-10-01/README.md).
Incluye recuperacion tras thinking-only, herencia de requisitos, contexto acotado,
recibos tras poda y payload real HTTP/SSE con handler simulado. No hay una nueva
validacion de POS completo ni inferencia remota en esta entrega.
