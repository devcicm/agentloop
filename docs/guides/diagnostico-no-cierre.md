# Diagnosticar una operacion sin cerrar

Guia vigente para `EventOperationCoordinator`. El [informe anterior](../archive/diagnostico-no-cierre-previo-2026-09-30.md)
conserva casos del ejecutor por pasos; `BreakReason: MaxIterations` y sus nudges
no describen el control operativo actual.

## Capturar el estado

En una operacion iniciada con texto libre o `run` dentro de `interactive`, usar
`estado`. `pausa`, `reanudar` y `cancelar` publican eventos aun si una herramienta
o el modelo estan esperando. Pausar impide nuevas acciones; no suspende procesos
ya lanzados. En modo confirmar, `aprobar` o `rechazar` decide el efecto pendiente.

El resultado terminal informa `Succeeded`, `Failed` o `Cancelled` y su motivo.
El deadline es un fracaso por presupuesto agotado; una cancelacion del operador
es distinta. El numero de solicitudes al LLM es un presupuesto, no el motor.

## Leer evidencia persistida

```powershell
dotnet run --project src/AgentLoop.Cli -- recuperacion ".agentloop/event-journal/<runId>.jsonl"
dotnet run --project src/AgentLoop.Cli -- diagnostico <runId> --journal-dir artifacts/journeys
```

`recuperacion` inspecciona el registro incremental: terminal, acciones admitidas
sin resultado y eventos recibidos sin confirmar. No reejecuta herramientas.
Devuelve 1 si hay pendientes; una operacion abortada puede estar reconciliada
sin haber cumplido su objetivo. Leer tambien el estado y motivo terminal.

`diagnostico` interpreta el snapshot de `journal`. Sus categorias son heuristicas;
el journal incremental y los resultados concretos permiten revisar la causa.
`goal` no genera automaticamente un snapshot Journey: consultar su JSONL.
El CLI interactivo tambien guarda el resumen en `.agentloop/operations/`.

## Recorrido de la causa

1. Ver el motivo terminal: deadline, cancelacion, falta de progreso, presupuesto
   de solicitudes, error del proveedor, fallo estructural o de registro.
2. Revisar admision: scope, argumentos, rutas, politica y apertura de tarjeta.
   Una herramienta rechazada debe tener resultado; no produjo el efecto solicitado.
3. Revisar resultados de herramientas y `receipt_id`. Los recibos deben pertenecer
   a la transaccion; memoria, narracion y datos de otra operacion no acreditan cierre.
4. Revisar las tarjetas rechazadas: `comprobar` exige `evidencia_id`; `cerrar`
   exige comprobacion vigente, archivos sin cambios posteriores y procesos finalizados.
5. Revisar el contrato de archivos: existencia, contenido, observacion, frescura,
   `--contains` y `--json` si se configuraron. Usar `--allow-existing` solo cuando
   un archivo anterior sea un resultado admisible. Estas opciones se aplican a `goal`.
6. Si el contexto fue rechazado, reducir antecedentes o resultados excesivos;
   no hay resumen automatico ni presupuesto exacto por tokens.
7. Si una accion queda incierta tras el drenado, comprobar su efecto real antes
   de repetirla. Un estado terminal de cancelacion no garantiza que una tool
   no cooperativa haya dejado de ejecutarse.

## Referencias comprobadas

[Validacion del actor](../architecture/implementacion-coordinador-eventos-2026-09-30.md):
340 comprobaciones registradas, incluidas evidencia falsa/alterada, control
con LLM pendiente, tools canceladas y observadores lentos.
[Traza NVIDIA](../evidence/2026-09-30/nvidia-poema-traza.txt): poema creado,
leido, comprobado y cerrado. No garantiza que el proveedor responda igual
ante todos los objetivos ni que un contenido libre sea semanticamente correcto.
