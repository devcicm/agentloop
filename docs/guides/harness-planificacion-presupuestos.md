# Harness: decisiones, presupuestos y revisi?n

Implementaci?n del 8 de octubre de 2026. El core mantiene pol?ticas y algoritmos generales; el kernel define puertos; el harness coordina eventos; infraestructura realiza efectos. Ning?n dominio de aplicaci?n es parte de estas pol?ticas.

## Flujo vigente

```text
Operador: objetivo + intenci?n + restricciones + criterios
                         |
                         v
Evento -> mailbox acotado -> coordinador -> tarjeta / estado
                              |
                  decisi?n: esperar / inferir / reconciliar / cerrar
                              |
                    contexto + skills confiables + grafo observado
                              |
                 ajuste de contexto -> reserva de unidades/coste
                              |
                    inferencia supervisada -> evento de respuesta
                              |
             herramienta -> admisi?n -> permiso -> ejecuci?n supervisada
                              |
                   resultado + journal + evidencia comprobable
                              |
                  progreso / criterio / incertidumbre -> evento
                              |
                    comprobar y cerrar / detener y reconciliar
```

Los bucles internos de lectura de streams, recorrido de grafos y despacho de colas son mecanismos de implementaci?n. Las transiciones del objetivo dependen de eventos: no de un contador de turnos ni de un sondeo peri?dico. Las inferencias y procesos devuelven eventos al coordinador. Pausa, cancelaci?n, permisos y plazos tienen prioridad de control. La salida repetida de procesos puede coalescerse; sus buffers conservan la salida. El historial y las colas tienen l?mites finitos. Un fallo de entrega o registro termina con un resultado fallido y exige revisar efectos ya admitidos.

## Pol?tica confiable

Sin archivo se usan l?mites generales. El archivo por defecto es `.agentloop/harness-policy.json` bajo la ra?z de la aplicaci?n, independiente del repositorio analizado. `AGENTLOOP_HARNESS_POLICY` permite seleccionar una ruta absoluta. Una ruta expl?cita inexistente, enlaces, campos desconocidos o l?mites inv?lidos impiden cargar la pol?tica. Los nombres JSON distinguen may?sculas.

Consultar [ejemplo de pol?tica](../../examples/harness-policy.example.json). Instalarlo es una decisi?n del operador; los ejemplos no se activan autom?ticamente. Las herramientas tipadas protegen la pol?tica, credenciales, memoria, sesiones y directorios de skills. Un shell nativo autorizado sigue siendo un proceso del usuario: esas protecciones no constituyen un sandbox. Usar `--no-shell`, scope de rutas o perfiles de ejecuci?n aislada para reducir el alcance. El backend Docker de proyectos existente usa red deshabilitada, sistema de archivos de solo lectura y l?mites declarados en el perfil externo.

Los l?mites incluyen eventos, hechos, llamadas HTTP, contexto, unidades acumuladas, diario, exploraci?n y acciones sin progreso. Repetir la misma acci?n y salida normalizadas no acredita avance indefinido. Los errores de transporte pueden reintentarse dentro de un presupuesto compartido; un efecto incierto no se reintenta autom?ticamente.

`ModelBudgets` permite ajustar ventana y tarifas por ID de modelo. La ventana predeterminada es una pol?tica conservadora, no una afirmaci?n sobre el contexto real del proveedor. No hay precios incorporados. `MaxCost` exige ambas tarifas generales; las entradas por modelo pueden sustituirlas. La moneda corresponde a las tarifas del operador. Sin tarifas se informa coste desconocido.

Antes de inferir se contabilizan mensajes, herramientas, instrucciones, hechos y espacio de respuesta. El estimador UTF-8 es una aproximaci?n, no un tokenizer universal. El uso reportado por el proveedor liquida la reserva; sin uso se conserva una carga estimada. Los reintentos sin uso reportado se cargan conservadoramente. Tokens de razonamiento incluidos en completion no se suman otra vez. El resultado muestra duraci?n, admisiones HTTP, unidades, coste y car?cter estimado.

## Intenci?n y memoria

En la consola:

```text
intencion Review
objetivo-fijar explicar arquitectura y dependencias del repositorio
restriccion citar archivo y versi?n observada
criterio ruta completa del artefacto esperado
skills Review
code-review --root "C:\ruta con espacios\repo" --max-files 64
repo-map --root "C:\ruta con espacios\repo"
ediciones
```

`General`, `Explain`, `Review`, `Edit`, `Build` y `Diagnose` son intenciones generales. Explain/Review vetan herramientas con efectos. Edit/Build necesitan criterios expl?citos para comprobar/cerrar. `RequireExplicitCriteria` extiende ese requisito a las dem?s intenciones. Una intenci?n restringe capacidades; no otorga permisos. `objetivo-fijar limpiar`, `restriccion limpiar` y `criterio limpiar` reinician sus respectivos datos. Las sesiones versi?n 2 guardan mensajes y estado expl?cito; se siguen leyendo sesiones anteriores. Los permisos continuos no se persisten.

Los par?metros de prompt admiten contratos Text/Integer/Boolean/Json en la API, origen, confianza, sensibilidad y l?mites. Los secretos reconocibles o par?metros sensibles se rechazan. El render no reinterpreta recursivamente valores como plantilla, produce Result y metadatos de caracteres, bytes y estimaci?n. La memoria incorporada tambi?n entra en el presupuesto de la solicitud completa. La etiqueta de contenido externo reduce ambig?edad; no garantiza resistencia absoluta a prompt injection.

## Skills y grafos

`SkillsDirectory` selecciona un directorio confiable de JSON, resuelto relativo al archivo de pol?tica. El cat?logo valida contratos, tama?o e IDs y selecciona por intenci?n y herramientas realmente disponibles. Sus procedimientos tienen presupuesto de llamadas. No se descubren instrucciones ejecutables desde archivos del proyecto; ninguna skill modifica admisi?n ni permisos. Consultar [ejemplo](../../examples/skills/review.example.json).

`project_map` y `code_review` inspeccionan archivos con presupuestos por archivo y totales. Devuelven hashes del contenido, fragmentos y referencias; pueden declarar cobertura parcial. S?mbolos extra?dos por expresiones regulares y referencias de proyecto son candidatos, no an?lisis sem?ntico verificado. Para explicar funciones o editar una l?nea se debe continuar con `code_view` y citar archivo, rango y hash. No hay ?ndice AST multilenguaje ni garant?a de comprender todo el repositorio. Las nuevas lecturas producen nuevas versiones; no se reutiliza un ?ndice persistente obsoleto.

El grafo de la operaci?n conserva acciones, recibos y versiones de recursos observados por el host; una relaci?n inventada por el modelo no se transforma en evidencia. El plan de proyectos usa un DAG de dependencias, invalidaci?n por descendientes y prioridad por duraci?n restante estimada.

`graph_plan` analiza un JSON declarado, sin ejecutar decisiones:

| Algoritmo | Uso y condici?n |
| --- | --- |
| Kahn | Orden topol?gico; rechaza ciclos |
| Kosaraju iterativo | Componentes fuertemente conexas y ciclos |
| Dijkstra | Ruta de menor coste no negativo con aristas admitidas/verificadas |
| A* | B?squeda guiada; optimalidad requiere heur?stica admisible |
| Camino cr?tico | Duraciones declaradas sobre DAG; no predice tiempos reales |
| Edmonds-Karp | Corte m?nimo con capacidades declaradas; no prueba aislamiento |
| AND/OR | Alternativas y requisitos, b?squeda acotada; costes compartidos pueden sobreestimarse |

Los algoritmos entregan recomendaciones. Los permisos, la aceptaci?n y la verdad de los efectos siguen dependiendo de contratos, observaciones y validaci?n. V?ase [grafo gen?rico](../../examples/graphs/dependencies.example.json).

## Edici?n y recuperaci?n

```text
code_view -> recibo de lectura + hash + rango
     |
edit_text_file preview=true -> patch_id + diff paginado + vencimiento
     |
revisi?n / permiso -> mismos argumentos + preview=false + patch_id
     |
verificar recibo / hash / selecci?n / firma / plazo
     |
respaldo durable protegido -> escritura -> journal -> nueva evidencia
```

Los reemplazos por l?neas requieren `old_text`; el modo patch admite cambios m?ltiples no superpuestos. El apply debe corresponder al preview vigente. Cambios de contenido invalidan recibos y propuestas. `write_text_file` conserva su contrato de lectura completa para sobrescritura; no sustituye el protocolo de preview de edici?n.

Los originales quedan bajo el almac?n dedicado `edit-backups`, con acceso restringido y comprobaci?n de exclusi?n de Git. Son copias en texto original, no cifradas. La cuota evita crecimiento ilimitado; al agotarse se requiere revisi?n/retenci?n manual. El journal ignorado `.agentloop/edit-recovery` conserva hashes y estado. No se publican respaldos. En paquetes sin checkout el almac?n predeterminado est? en LocalApplicationData/AgentLoop/local-secrets.

`ediciones` muestra originales, cambios e incertidumbre. `ediciones restaurar <id> --hash-current <hash>` exige confirmaci?n interactiva `RESTAURAR <id>` y comprobaci?n del hash actual; guarda tambi?n un respaldo de deshacer. Las escrituras existentes no son una transacci?n at?mica de sistema de archivos. Un corte durante escritura/restauraci?n puede exigir reconciliaci?n usando los respaldos; nunca se afirma ?xito por mera intenci?n del modelo.

## Verificaci?n y l?mites

La evidencia de esta implementaci?n es compilaci?n Release y auditor?as est?ticas de arquitectura, secretos y diff. No se ejecutaron pruebas automatizadas ni inferencias remotas nuevas. Los informes de pruebas anteriores y ZIP anteriores corresponden a su revisi?n original. No validan estas modificaciones. No se public? una versi?n.

Quedan como ampliaciones: ?ndices sem?nticos multilenguaje, tokenizer exacto por proveedor, m?tricas operativas de larga duraci?n, transacciones at?micas generales y aislamiento uniforme de cualquier shell. El comportamiento con proveedores reales y carreras de cancelaci?n requiere pruebas espec?ficas antes de una publicaci?n.

## Actualización de calidad — 2026-10-11

La suite sin red cubre ahora el contrato de transporte: el endpoint debe ser
HTTPS y de un origen permitido, un 5xx puede reintentarse y todos los intentos
consumen una misma cuota. No usa credenciales ni llama a NVIDIA.

`AgentLoop.Harness` ofrece `ActivitySource` y `Meter` para exportadores que el
host instale. Sus etiquetas solo incluyen intención, estado, resultado y
conteos; nunca llevan prompts, argumentos de tools, rutas o secretos. El
Journey suma contadores de operaciones, modelo, tools, unidades y solicitudes
HTTP.

La edición recuperable falla cerrada si el sistema no permite crear el respaldo
con ACL privada. En ese caso no modifica el archivo original. La migración a
.NET 10 LTS sigue pendiente porque esta máquina solo dispone del SDK 9.0.316.
