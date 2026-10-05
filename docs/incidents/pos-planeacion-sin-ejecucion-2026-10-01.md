# Incidente: POS planeado sin ejecucion de fase 1

Diagnostico y correccion: 1 de octubre de 2026. Captura base del repositorio:
commit 6d0e954, rama feat/harness-eventos-tarjetas, arbol limpio antes de cambios.
El proyecto solicitado era un POS de supermercado HTML/JS/CSS, servidor MJS,
HTTP/REST y SQLite en Desktop/pos_system. Este informe documenta el fallo del
harness; no declara creado ni probado el POS completo.

## Antes: evidencia observada

Operacion local: `.agentloop/operations/20260930-101436-124eb70c5bec497eae90a42d8b17838e.json`.
Journal: `.agentloop/event-journal/6121f3c05ab14f318dce170150c5add8.jsonl`.
Extracto reproducible con hashes: [pos-before.json](../evidence/2026-10-01/pos-before.json).
No se publican el hilo completo ni los dumps de carpetas personales.

| Paso | Resultado real |
| --- | --- |
| Modelo 1 | abrir transaccion; tool_calls |
| Modelo 2 | ejecutar node --version; exito, Node v24.14.0 |
| Modelo 3 | finish_reason=length; 2048 completion, 2048 reasoning; sin contenido/tools |
| Modelo 4 | finish_reason=length; 4096 completion, 4096 reasoning; sin contenido/tools |
| Modelo 5 | cancelado al vencer 300 segundos |

Solo dos llamadas de herramientas: tarjeta y ejecutar. Ninguna escritura,
servicio iniciado o prueba HTTP. Tarjeta abierta sin cierre. Directorio objetivo
inexistente en la inspeccion local previa. Usage agregado: prompt=54720,
completion=6828, reasoning=6563, total=61548. Estado Failed por deadline.
Journal: 31 received, 31 applied, 7 admitted, 7 resolved, 1 terminal.
El actor registro resultados y cancelo: esta evidencia no muestra fallo del bus.

## Causas

1. Control GLM equivocado: se enviaban thinking.budget_tokens=256 y enable_thinking;
   eso no constituia un limite efectivo de razonamiento de GLM 5.3. NVIDIA define
   reasoning_effort low/high/max y default max. El resultado observado consume toda
   la salida en pensamiento. [Referencia NVIDIA](https://docs.api.nvidia.com/nim/reference/z-ai-glm-5-3).
2. Recuperacion elevaba tokens de salida pero mantenia la misma estrategia de
   razonamiento. De 2048 paso a 4096 y volvio a truncarse sin accion.
3. Contexto: system tenia 34235 caracteres, incluido JSON del hilo con pensamientos,
   ASCII y listados. La poda conservaba ese bloque y eliminaba resultados recientes.
4. Objetivo de admision era solo una aprobacion del plan. La especificacion POS
   estaba enterrada en antecedentes, sin contrato estable de artefactos completo.

```text
ANTES DEL FIX DE ESTA ENTREGA
aprobacion -> system con historial entero -> abrir -> node --version
                                                    |
                                razonamiento agota 2048 tokens
                                                    |
                              duplicar tokens, mismo control
                                                    |
                                razonamiento agota 4096 tokens
                                                    |
                                       deadline -> Failed
```

## Solucion implementada

- Provider separado en Contracts, Helpers, Transport y Nvidia; puerto ILLMClient.
- Adaptador GLM envia reasoning_effort=low y clear_thinking=true en HTTP/SSE;
  no promete budget numerico ni apagado. No degrada silenciosamente al default max.
- Builder fija requisitos recientes de usuario para aprobaciones reconocidas,
  prioriza instruccion actual y limita plantilla/antecedentes, sin reasoning previo.
- Resultado de cada tool alimenta indice acotado de observaciones con receipt_id,
  independiente del historial podado; journal conserva el resultado completo.
- Recuperacion sin tools exige una accion concreta, pequenas escrituras y salida
  acotada; vuelve por un evento explicito. Deadline y control siguen independientes.
- Journal registra los controles y tamano del contexto admitido para diagnosticar.

```text
DESPUES IMPLEMENTADO
aprobacion -> requisitos fijos y acotados -> evento objetivo
                                      |
                           GLM low + clear_thinking
                                      |
                     propuesta de herramienta / resultado
                                      |
                si truncado sin accion: evento recuperacion
                tool requerida + un archivo por llamada
                                      |
                       admision -> efecto -> recibo real
                                      |
                     comprobar -> criterio/hash -> cerrar
```

Arquitectura completa, contexto, eventos, tarjetas, proveedores y limites:
[harness vigente](../architecture/harness-eventos.md). Mapa de archivos: [Provider](../../src/AgentLoop.Infrastructure/Providers/README.md).

## Comprobacion y pendientes

La regresion sin red reproduce un truncamiento de solo pensamiento despues de
abrir, comprueba solicitud de recuperacion y crea un artefacto real en un fixture;
valida recibo y cierre. Contratos HTTP/SSE prueban el payload que construye el
cliente NVIDIA mediante un handler local. La evidencia se conserva en
[2026-10-01](../evidence/2026-10-01/README.md).

Esto acredita codigo y contratos locales. No se ha vuelto a ejecutar el POS
original con NVIDIA: el efecto sobre ese proyecto requiere una nueva operacion
con manifiesto explicito de archivos y pruebas funcionales. Presupuesto de 300s,
calidad del contenido, requisitos recortados y dialecto remoto Kimi siguen siendo
limites concretos; el bus por eventos no garantiza la terminacion de cualquier tarea.
