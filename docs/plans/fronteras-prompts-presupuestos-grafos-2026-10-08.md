# Fronteras, prompts, presupuestos y grafos

Estado: propuestas tras lectura del codigo, sin pruebas ni cambios de runtime.
Amplia el [backlog agentico](mejoras-agenticas-2026-10-07.md). El motor mantiene
eventos, tarjetas, permisos y evidencia como autoridad de ejecucion.

## Lista consolidada

| Prioridad | Frontera pendiente | Propuesta |
| --- | --- | --- |
| Critica si se altera configuracion | Endpoint configurable recibe el header de credencial | Destinos autorizados y HTTPS por descriptor confiable |
| Alta | Entrega de resultados fuera de captura y tareas sin supervision | Supervisor de tareas y entrega durable con errores tipados |
| Alta | Suscriptores del bus invocados sin aislamiento individual | Resultado de entrega por suscriptor y recuperacion |
| Alta | Herramientas repetidas reinician contador text-only | Progreso material, fingerprints y criterio de estancamiento |
| Alta | Efectos inciertos fuera de proyectos no tienen barrera equivalente | Reconciliacion general antes de repetir |
| Alta | Recibos sin criterios pertinentes pueden ser evidencia insuficiente | Contrato de aceptacion por intencion y evidencia por criterio |
| Alta | Mailboxes y conjuntos de eventos sin limite explicito | Backpressure, prioridades y agrupacion de salida |
| Media | Reintentos de transporte y reactor acumulan intentos | Presupuesto compartido de intentos, tiempo y coste |
| Media | Sinks de registro pueden terminar una operacion tras efectos | Registro obligatorio separado de observers; estado incierto |
| Media | Observacion de operacion usa duracion cero | Duracion real y relacion entre accion, inferencia y resultado |
| Media | Result permite default y acceso nullable directo | Invariantes y consumo explicito; el compilador no exige Match |
| Media | Intencion de continuacion depende de regex y cuatro mensajes | Estado de objetivo, restricciones y decisiones versionadas |
| Alta | Variables interpoladas sin contrato de confianza/sensibilidad | Variables tipadas, procedencia y exclusion de secretos |
| Alta | Expansion de placeholders repetidos y escapes sin presupuesto agregado en Render | Medir salida renderizada; limitar expansion antes de asignaciones grandes |
| Alta | Limites en caracteres, bytes y estimaciones no son intercambiables | AdmissionBudget comun para cada operacion y llamada |
| Media | Estimador UTF-8 conservador puede descartar contexto util | Estimador por modelo cuando disponible y margen configurable; declarar Exact=false |
| Media | Falta contabilidad unificada de coste y reservas de reintentos | BudgetLedger por operacion, proveedor y modelo, sin precios incrustados |
| Media | Grafo de dependencias parcial, sin mapa del codigo y evidencia integrado | Grafos tipados, procedencia, revision e invalidacion incremental |
| Media | Seleccion de trabajo por primer habilitado, sin funcion de coste | Planificador configurable con dependencias y presupuestos |
| Media | No hay resolver de skills operativas | Skills versionadas con contratos, permisos, presupuestos y criterios |
| Alta | Explicacion del repo sin cobertura/procedencia explicita | Revision con referencias de archivos/simbolos, alcance inspeccionado y distincion entre observado e inferido |
| Alta | Revision y reemplazo de codigo sin contrato semantico comun | Objetivo de cambio, rango/version leidos, precondiciones, diff, relectura y validacion pertinente |
| Media | Vista de codigo duplica contenido y texto numerado | Presupuesto de respuesta y representacion para modelo configurable, conservando numeros de linea y recibos |
| Media | Edicion existente escribe sobre el archivo abierto y revierte best-effort | Definir recuperacion durable de parches y tratamiento de caidas durante escritura; no afirmar atomicidad |

## Interpolacion: existente y faltante

`PromptTemplate` reconoce `{{variable}}` y modos plain/xml/json/raw; rechaza
variables ausentes. `PromptVariables.Set` sanea y recorta cada valor. El modo raw
no evalua codigo y usa el valor previamente almacenado: no es ejecucion de shell.
Escapar sintaxis no evita que un texto externo contenga instrucciones maliciosas.

Agregar:

- `PromptVariableSpec`: tipo, origen, nivel de confianza, sensibilidad, limite
  y politica de truncamiento. Secretos no son variables de prompt.
- `PromptRenderResult`: plantilla/version, variables usadas por nombre,
  caracteres, bytes, estimacion de tokens y avisos; sin guardar valores sensibles.
- Limite del resultado total despues de sustituciones y escapes. Un valor
  permitido repetido muchas veces puede producir una salida desproporcionada.
- Error explicito para truncar contratos, JSON o restricciones; el recorte de
  texto libre puede ser permitido con aviso y referencia al recurso completo.
- Bloques externos delimitados como datos, separados de instrucciones confiables.
  La sanitizacion es una capa; la admision sigue perteneciendo al host.

## Presupuesto: unidades distintas

El proyecto ya dispone de `ITokenBudgetEstimator`, `MaxInputTokens` y
`MaxOutputTokens`. `Utf8TokenBudgetEstimator` cuenta bytes, declara `Exact=false`
y se presenta como cota conservadora; no representa el tokenizer del proveedor.
`ProjectContextBudget` mide el request serializado completo y retira intercambios.
Las operaciones genericas tambien usan limites en caracteres.

Registrar por separado:

```text
Prompt renderizado
  -> caracteres (legibilidad y limites de texto)
  -> bytes UTF-8 (transporte y almacenamiento)
  -> tokens estimados + metodo/version/margen
  -> reserva de salida y razonamiento segun contrato del modelo
  -> admision contra ventana de contexto configurada
  -> reserva de tiempo, llamadas y coste
  -> uso observado del proveedor -> ajustar reservas
```

No usar una conversion universal caracteres/tokens. El uso observado puede
incluir razonamiento dentro de completion: no sumarlo de nuevo sin conocer el
contrato. Una tarifa cero no elimina cuotas, latencia ni limites de contexto.
Si faltan tarifas o usage, registrar desconocido/estimado; no inventar coste cero.
Las reservas deben liberarse/conciliarse incluso en cancelaciones y timeouts.

## Grafos para casos concretos

No imponer un DAG al mapa completo: el codigo puede tener ciclos. El grafo de
tareas si debe rechazar dependencias ciclicas. `ProjectContractAdmission` ya hace
esa comprobacion y `ProjectState.EnableNext` selecciona el primer habilitado.

| Modelo o algoritmo | Caso propuesto | Condicion/limite |
| --- | --- | --- |
| DAG y orden topologico (Kahn/DFS) | Habilitar tareas tras aceptar dependencias | Requiere grafo aciclico |
| Alcanzabilidad/descendientes | Invalidar validaciones y tareas afectadas por cambios | Aristas deben expresar la direccion de impacto |
| Componentes fuertemente conexas (Tarjan) | Detectar dependencias circulares del codigo o recorridos de decisiones repetidos | Un ciclo puede ser valido; diagnosticar antes de prohibir |
| Camino mas largo en DAG / ruta critica | Priorizar cadenas que condicionan el tiempo total | Duraciones estimadas; recursos compartidos cambian el problema |
| Dijkstra o A* | Seleccionar secuencia de acciones hasta criterios satisfechos | Dijkstra exige pesos no negativos; A* necesita heuristica admisible para garantizar optimalidad |
| Grafo AND/OR | Distinguir requisitos que deben cumplirse todos de alternativas de solucion | Un camino simple no representa requisitos AND |
| Flujo maximo/corte minimo | Analizar fronteras de acceso o separaciones de dependencias | Capacidades deben tener significado; no acredita aislamiento real |

Las aplicaciones de la tabla son propuestas para este harness, no garantias de
que un algoritmo encuentre la mejor solucion en un entorno incierto. El coste
puede incluir tiempo, tokens y riesgo, con unidades normalizadas y pesos
configurados. Las acciones prohibidas se excluyen antes de optimizar; no se
compensan permisos faltantes con una puntuacion menor.

Referencias primarias de algoritmos:
[DAG, orden topologico y caminos](https://networkx.org/documentation/stable/reference/algorithms/dag.html),
[Tarjan y componentes fuertes](https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.components.strongly_connected_components.html),
[A* y limites de la heuristica](https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.shortest_paths.astar.astar_path.html),
[flujo/corte minimo](https://networkx.org/documentation/stable/reference/algorithms/generated/networkx.algorithms.flow.minimum_cut.html).
Estas referencias no implican agregar NetworkX ni cambiar el lenguaje del repo.

## Esquema y flujo propuesto

```text
Mapa del proyecto
  Archivo -> define -> Simbolo -> usa -> Simbolo
  Tarea   -> requiere -> Tarea/Artefacto
  Criterio -> requiere -> Evidencia vigente
  Accion -> produce/modifica -> Recurso
  Evidencia -> verifica -> Criterio

Evento
  -> actualizar nodos/aristas afectados y su revision
  -> resolver intencion y criterios pendientes
  -> obtener subgrafo pertinente (sin cargar todo el repo al prompt)
  -> habilitar alternativas por dependencias y permisos
  -> decidir con algoritmo/politica del host
       -> decision conocida -> accion admitida
       -> incertidumbre -> render acotado + presupuesto -> inferencia
  -> resultado -> evidencia -> nuevo evento
  -> invalidar conocimiento obsoleto y evaluar cierre
```

Cada arista debe indicar tipo, fuente, revision/hash y confianza. Las relaciones
inferidas por el LLM son hipotesis hasta verificarlas; referencias de codigo no
demuestran por si solas causalidad. Las skills aportan procedimientos reutilizables
al planificador y nunca amplian permisos.

## Orden de implementacion

1. Supervision de entregas, efectos inciertos y progreso material.
2. Contratos de variables y presupuesto despues del render, incluyendo schemas.
3. Contabilidad comun de llamadas/uso y reservas.
4. Grafo de dependencias y evidencia con invalidacion incremental.
5. Politica de intenciones, subgrafo de contexto y skills.
6. Busqueda de alternativas ponderadas cuando exista informacion suficiente.

Validaciones futuras: expansion repetida, escapes, Unicode, campos sensibles,
presupuestos agotados, tokens desconocidos, ciclos, invalidacion de descendientes
y decisiones sin ruta valida. No se ejecutaron pruebas en esta ampliacion documental.

## Code review: conocer y explicar el proyecto

Separar dos solicitudes del operador: explicar que hace el codigo y revisar sus
defectos. Ambas requieren lectura y referencias; una explicacion no autoriza
escrituras. El alcance debe indicar revision Git, carpetas inspeccionadas y partes
no examinadas. No afirmar cobertura completa por leer el README.

Procedimiento propuesto, parametrizable por lenguaje y herramientas disponibles:

1. Inventariar archivos pertinentes, manifiestos, puntos de entrada y dependencias,
   excluyendo secretos, binarios, generados y datos privados.
2. Construir/consultar el grafo de archivos, simbolos, llamadas y recursos con
   procedencia y hash. Los resultados de un parser y los del LLM conservan su origen.
3. Leer el subgrafo que explica la funcionalidad solicitada dentro del presupuesto.
4. Responder con proposito, entradas/salidas, recorrido de ejecucion, efectos,
   limites y referencias de archivo/linea de la revision inspeccionada.
5. Para revision de defectos, emitir hallazgos con severidad, evidencia, condiciones
   necesarias y propuesta. Diferenciar defecto confirmado, riesgo e hipotesis.
6. Entregar `Result<CodeReviewReport, Failure>`; validar cobertura minima del
   contrato de revision. La respuesta del LLM no acredita una prueba ejecutada.

El reporte puede declarar `purpose`, `entryPoints`, `flows`, `dependencies`,
`effects`, `findings`, `references`, `coverage` y `unknowns`. Son DTOs propuestos,
no tipos ya implementados. Contexto externo del repo se considera dato no confiable.

## Edicion de lineas especificas: capacidad existente y ampliacion

`src/AgentLoop.Infrastructure/Files/Editing/TextEditingTools.cs` ya implementa:

- `code_view`: lectura paginada con numeracion, hash y `read_receipt`.
- `edit_text_file` con `mode=lines`: rango inclusivo desde 1, comprobacion del
  recibo/rango/version, `old_text` opcional y preview.
- Rechazo de documentos modificados desde la lectura, conservacion de encoding
  y saltos, e invalidacion de recibos tras aplicar.

Ejemplo de propuesta para una herramienta, no comando directo del prompt CLI:

```json
{
  "path": "src/modulo.cs",
  "read_receipt": "<recibo real de code_view>",
  "mode": "lines",
  "start_line": 42,
  "end_line": 45,
  "old_text": "<contenido exacto previamente leido>",
  "new_text": "<reemplazo propuesto>",
  "preview": true
}
```

No inventar recibos ni editar con numeros de linea de otra version. Leer el rango
completo, incluidos sus saltos. Para codigo, proponer `old_text` obligatorio o
selector por simbolo con precondicion de hash, configurable por contrato. Un
reemplazo textual no equivale a un refactor semantico.

El preview actual muestra extractos de hasta 512 caracteres, no un diff completo.
Agregar un patch/diff paginado con identificador, hash base, rangos y efectos.
El permiso debe vincularse al patch y su version; una confirmacion de preview no
debe autorizar una propuesta distinta. Aplicar cambios multiples de atras hacia
adelante o como patch de la misma version para evitar desplazar rangos pendientes.

```text
Solicitud de explicacion/revision
  -> scope -> mapa del repo -> lectura pertinente
  -> reporte con referencias y cobertura
       |
Solicitud autorizada de cambio
  -> leer codigo y contexto -> recibo/version/rango
  -> proponer patch y diff completo -> comprobar precondiciones
  -> admision y permiso cuando corresponda
  -> aplicar -> nuevo hash -> invalidar recibos y grafo afectado
  -> releer -> validadores autorizados por contrato
  -> evidencia del cambio y sus limites -> cierre
```

El editor usa escritura sobre un handle exclusivo con rollback best-effort.
Una caida del proceso o sistema durante escritura no equivale a una transaccion
atomica; requiere recuperar/reconciliar. Las herramientas tipadas no impiden
que shell arbitrario evada este protocolo. Validaciones futuras: rango no leido,
version cambiada, desplazamiento de lineas, preview truncado, patch alterado,
Unicode/encoding, permisos y fallos durante escritura. No se ejecutaron pruebas.

## Implementaci?n aplicada

La implementaci?n de eventos, presupuestos, intenciones, contratos de prompt, skills confiables, grafos, revisi?n y edici?n recuperable se describe en [la gu?a vigente](../guides/harness-planificacion-presupuestos.md). [Evidencia y l?mites de verificaci?n](../evidence/2026-10-08-harness-planificacion/README.md). El backlog conserva propuestas de ampliaci?n; no equivale a afirmar que existe AST multilenguaje, tokenizer exacto, aislamiento de cualquier shell o verificaci?n remota completa.
