# Inventario y limpieza de datos del repositorio

Revision sobre la base `8b20a3f`, rama `feat/harness-eventos-tarjetas`.
Limpieza iniciada el 30 de septiembre; cierre documental el 1 de octubre de 2026.
Alcance: todos los archivos versionados y nuevos no ignorados; metadatos de las
carpetas locales. Los secretos se clasifican como credenciales locales sin
inspeccionar ni copiar su contenido. La revision documental no es una nueva
prueba del proveedor ni una nueva ejecucion de la suite.

## Hallazgos procesados

| Hallazgo | Tratamiento |
| --- | --- |
| No existia README ni indice documental | Creados mapa del codigo, entradas, contratos e indice vigente/historico |
| Informes antiguos decian "actual" | Identificados como historicos y enlazados al coordinador vigente |
| Diagnostico de cierre basado en StepAsync/MaxIterations | Guia actual reescrita; texto previo conservado en `docs/historico/` |
| Ejemplos de recursos usaban chat ambiguamente | Ejemplos de `run`, distincion de modos y cwd real frente a `--cwd` |
| Instrucciones Azure ajenas al proyecto | Sustituidas por instrucciones del harness NVIDIA por eventos |
| Comentarios sobre cuota gratuita y proveedor anterior | Alineados con configuracion NVIDIA; nombres de tipos preservados por compatibilidad |
| Procedencia de credenciales documentada incorrectamente | Orden efectivo documentado; mensaje de entorno legado identificado con su nombre real |
| Perfil Visual Studio etiquetado "loop agentico" | Renombrado a "objetivo por eventos" |
| Cache de modelos TokenRouter del 11 de septiembre | Archivado fuera de la ruta activa; se conserva hash y procedencia |
| Logs locales con copia versionada equivalente | Retirados tras comparar el texto normalizado y registrar hashes |
| Salidas bin/obj regenerables | Retiradas con rutas absolutas verificadas dentro del workspace |
| Manifiesto historico cuyo informe cambio | Conservado como foto historica; separado del inventario vigente |
| `after-fix-probe.json` mezclaba consola y JSON | Bytes originales preservados en `after-fix-probe-console.txt`; JSON extraido sin volver a ejecutar la sonda |
| Archivo patch vacio | Conservado y explicado: captura de ausencia de diff, no evidencia perdida |
| Duplicados exactos entre archivos Git | Ninguno en los 150 archivos de la base |

## Datos locales y retencion

- `local-secrets/`: credenciales operativas, ignoradas por Git. No son archivos de limpieza.
- `.agentloop/memory.json`, `session.json` y `operations/`, cuando existen:
  memoria y registros operativos; se conservan. No acreditan otra operacion.
- `.agentloop/event-journal/`: evidencia incremental; se conserva, incluso si
  contiene una operacion cancelada o incierta. No se repiten efectos para limpiarla.
- `.agentloop/archive/2026-09-30/`: cache obsoleto archivado, sin seleccion activa.
- `artifacts/journeys/`: capturas anteriores; se conservan como datos historicos.
- `.vs/`: estado del IDE, ignorado y regenerable; se conserva para no alterar
  la sesion de Visual Studio. Su contenido no se confunde con evidencia del harness.
- Los intermediarios locales no equivalentes a una copia versionada permanecen
  como datos de trabajo identificados; no se eliminan por una supuesta falta de utilidad.

La retirada del catalogo antiguo hace que, en ausencia de `--model` y de un cache
nuevo de `discover`, se use el fallback NVIDIA `z-ai/glm-5.3`. No se fabrica un
catalogo ni se declara que un sondeo remoto nuevo haya sucedido.

## Informacion procesada y limites

[Operaciones de limpieza](limpieza-operaciones-2026-09-30.json) registra cada
retirada/archivo, rutas, hashes, bytes disponibles y motivo. La longitud del
log local NVIDIA no se capturo antes de retirarlo; se conservan su hash original
y la copia textual equivalente con la linea de credencial omitida. El
[inventario actual](repository-data-inventory.json) clasifica los archivos,
comprueba enlaces locales, sintaxis JSON/JSONL y grupos de duplicados exactos.
Excluye su propio hash para evitar una referencia circular.

El codigo compilado y las bibliotecas de soporte se conservan. Compilar un
archivo no prueba que todas sus APIs esten en el camino principal; decidir que
una API publica sobra exige una revision de consumidores, no contar enlaces.
Las utilidades de compatibilidad y sus fixtures quedan identificadas.

Resultado del inventario de esta entrega: **158 archivos clasificados**, cero
enlaces locales rotos, cero errores de lectura JSON/JSONL y cero grupos de
duplicados exactos. El propio inventario queda fuera del conteo y hash por la
referencia circular. Se retiraron al menos **2.819.876 bytes** medidos; el
catalogo archivado ocupa 22.450 bytes y mantiene su SHA-256 original.

Persisten los gaps funcionales ya publicados en el
[informe de implementacion](implementacion-coordinador-eventos-2026-09-30.md):
recuperacion automatica reconciliada, limites conjuntos de colas y validadores
semanticos. Ademas `journal` no transmite todas las opciones que analiza el CLI
(por ejemplo stream/politica), y `--json`/`--allow-existing` no estan en la lista
de flags booleanos del parser posicional: usar las opciones despues del objetivo
como muestran las guias. Se documentan; esta limpieza no cambia esos contratos.

## Repetir el inventario

```powershell
powershell -File scripts/audit-repository.ps1
# Por defecto escribe un informe local ignorado, no elimina archivos.
powershell -File scripts/audit-repository.ps1 -OutputPath docs/repository-data-inventory.json
```

Salida 0 significa que no se detectaron enlaces locales rotos ni documentos
JSON/JSONL invalidos dentro del alcance. La deriva del manifiesto historico se
informa aparte; no se reescribe la evidencia original para ocultarla.
