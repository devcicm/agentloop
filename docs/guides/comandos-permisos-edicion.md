# Comandos, permisos y edicion de documentos

Implementacion: 2026-10-05. Motor principal: `EventOperationCoordinator`.

## Scripts con varias lineas

`ejecutar` y `lanzar` reciben un script completo en `comando`. En el JSON,
`\n` representa un salto real despues de decodificar. PowerShell y pwsh
reciben UTF-16LE mediante `-EncodedCommand`: el host clasifica primero el
script original. Bash/sh conservan el script como un argumento independiente.
Cmd no entiende here-strings de PowerShell.

Ejemplo del campo `comando`, ya decodificado:

```powershell
$memo = @'
Primera linea del memorando.
Segunda linea.
'@
$memo | Set-Content -LiteralPath 'C:\proyectos con espacios\memo.txt' -Encoding utf8
```

El delimitador de cierre debe respetar la sintaxis del interprete. El
transporte conserva las comillas y los saltos; no corrige scripts invalidos.

## Permisos del operador

| Caso | Resultado |
| --- | --- |
| Comando vetado por politica | Rechazo, incluso con permiso continuo |
| `auto`, comando sin indicadores de riesgo | Ejecucion sin pregunta |
| `confirmar`, comando ordinario | Pregunta, salvo permiso continuo vigente |
| Destructivo u opaco detectado | Aprobacion puntual, tambien en `auto` |
| Sin clasificador instalado | Se considera opaco y exige aprobacion puntual |
| `simular` | No ejecuta efectos ni acredita su realizacion |
| Shell deshabilitado o scope de rutas | Rechaza shell arbitrario; usar tools tipadas |

En interactive:

```text
aprobar                         una solicitud unica pendiente
aprobar <approval_id>            una solicitud concreta
aprobar <approval_id> continuo   permiso continuo si el riesgo lo permite
rechazar <approval_id>           rechaza esa solicitud
permisos                        consulta autorizaciones
permisos revocar                elimina permisos continuos
```

En `goal`, la pregunta ofrece `s` (una vez), `c` (continuo, cuando aplica) y
`N` (rechazar). Sin entrada interactiva, `goal` rechaza las solicitudes.

El permiso continuo vive en memoria en la politica de la sesion y se acota
al interprete y directorio de trabajo. No se guarda en disco ni se recupera
del journal. Autoriza comandos ordinarios dentro de ese alcance; no equivale
a una restriccion de los archivos que el script puede alcanzar. Durante una
operacion, revocar publica un evento y afecta a futuras admisiones; no detiene
procesos ya autorizados. En `confirmar`, la autorizacion de escritura tipada
tambien consulta ese alcance. Una solicitud destructiva/opaca no puede crear
permiso continuo: aprobarla con `continuo` autoriza solo esa ejecucion.

Las solicitudes incluyen ID, script, shell, cwd, riesgo y motivo. Una decision
consume su solicitud concreta; decisiones repetidas no autorizan otro comando.

```text
HerramientaSolicitada
       |
       v
admision / vetos / clasificador del host
       |
       +---- permitido o simulado ----> resultado
       |
       v
AprobacionSolicitada(id, script, riesgo, scope)
       |
       v
espera asincrona TaskCompletionSource (sin sondeo)
       ^
       |
operador --> Aprobacion(id, decision, continuo)
       |
       +---- rechazo ----------------> error sin ejecucion
       |
       v
proceso --> eventos de salida/fin --> HerramientaResultado --> journal
```

El clasificador es una heuristica configurable mediante
`ICommandRiskClassifier` y los patrones de `ShellCommandRiskClassifier`.
Incluye borrados, perdida de cambios Git, terminaciones, escritura/redireccion,
SQL destructivo y codigo anidado/codificado. Puede preguntar por texto que
parece un comando dentro de un here-string y puede omitir formas indirectas
de producir efectos. No es un parser completo ni un sandbox. Para exigir
aprobacion de cada script, usar `--politica confirmar` sin permiso continuo.

## Leer antes de editar

Las tools de documentos comparten un `TextEditingTools` por registro:

- `read_text_file`: texto paginado y recibo de lectura.
- `code_view`: lo anterior y vista con numeros de linea en consola.
- `edit_text_file`: reemplazo literal de texto, palabra, lineas o parrafo.
- `write_text_file`: crea archivos; para sobrescribir exige lectura completa.

Un `read_receipt` es distinto de un `receipt_id` de evidencia de tarjetas.
El primero habilita editar una version/rango leidos; el segundo acredita
una observacion exitosa de la transaccion.

```text
code_view / read_text_file
       |
       v
lectura --> contenido + SHA-256 + read_receipt + rango + pagina siguiente
       |
       v
edit_text_file(path, read_receipt, modo, reemplazo)
       |
       v
scope / archivo / hash vigente / rango leido / coincidencias exactas
       |
       +---- invalido ----> error sin escribir; leer de nuevo
       |
       +---- preview ----> propuesta, sin evidencia de efecto
       |
       v
escritura con handle exclusivo --> invalida recibos del archivo
       |
       v
code_view --> observar --> comprobar tarjeta --> cerrar
```

Ejemplo de lectura:

```json
{"path":"documento.txt","start_line":1,"line_count":40}
```

Usar el `read_receipt` real devuelto, sin inventarlo:

```json
{"path":"documento.txt","read_receipt":"<recibo devuelto>","mode":"word","old_text":"anterior","new_text":"nuevo","expected_matches":1,"preview":true}
```

Repetir con `preview:false` para aplicar. Los modos:

| Modo | Seleccion |
| --- | --- |
| `text` | `old_text` literal dentro del rango leido |
| `word` | Palabra completa; limites Unicode, sin espacios en `old_text` |
| `lines` | `start_line`/`end_line` inclusivos desde 1; `old_text` opcional como precondicion |
| `paragraph` | `old_text` entre lineas en blanco o bordes del documento |

Para texto/palabra/parrafo, `expected_matches` vale 1 por defecto. Una
cantidad diferente produce error y pide precisar la seleccion. Las lineas
seleccionadas deben haberse leido completas, incluidos sus saltos.

Seguir `next_line`/`next_column` cuando `truncated` sea verdadero. Lecturas
contiguas o solapadas del mismo hash amplian el alcance del recibo mas reciente;
un hueco no leido nunca se autoriza. Sobrescribir exige alcance 0 hasta
`document_characters`. Si cambia cualquier byte, el recibo deja de ser valido.

Limites configurables en `TextEditingOptions`: 1 MB por documento, 4096
caracteres por pagina, hasta 200 lineas y 128 recibos por registro por defecto.
El editor reconoce UTF-8 y UTF-16 con BOM, conserva codificacion/BOM y adapta
los saltos del reemplazo al documento. Rechaza binarios/NUL y codificaciones
no admitidas. Columnas y rangos internos cuentan unidades UTF-16.

La verificacion y escritura de archivos existentes usan el mismo handle
exclusivo. Ante error se intenta restaurar la version original; una caida
del proceso/equipo no garantiza rollback. Un error posterior al comienzo
del efecto queda incierto, requiere reconciliacion y nunca cuenta como exito.
Archivos nuevos usan temporal y movimiento sin sobrescritura.

La lectura previa se exige en las tools tipadas. Un shell arbitrario puede
escribir por otras vias: usar `--no-shell --scope-root <ruta>` si se necesita
obligar al agente a este protocolo de archivos.

## Capas y estado de entrega

Kernel declara el evento `PermisosRevocados`. Harness define el puerto de
riesgo, decisiones, admision y aprobaciones por eventos. Infrastructure
implementa clasificacion, transporte del script y editor. CLI presenta
solicitudes, controles y code view; el modelo no concede permisos.

La solucion compila sin errores ni advertencias. No se ejecutaron pruebas
funcionales ni llamadas remotas en esta entrega; la compilacion no demuestra
el comportamiento de los interpretes ni el consentimiento en ejecucion.
