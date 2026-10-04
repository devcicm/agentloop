# Indice de documentacion

Documentacion actualizada al 3 de octubre de 2026: [Core/Kernel y perfiles externos](core-kernel.md).
[Compilacion actual](evidence/2026-10-03-core-kernel/README.md). Las 415 comprobaciones
y el POS corresponden a la revision anterior 5132301; no se reejecutaron aqui.
Los informes fechados conservan su revision de origen.

## Guias vigentes

| Documento | Alcance |
| --- | --- |
| [Core/Kernel y host](core-kernel.md) | Bibliotecas independientes, puertos, perfiles y contexto |
| [Goals de proyectos por eventos](goals-proyectos.md) | Contratos, tareas, validadores, aislamiento, contexto y recuperacion |
| [Seguridad, datos y DTOs](seguridad-datos-harness.md) | Sanitizacion, contratos, limites de confianza y uso acotado |
| [Harness vigente y flujos ASCII](harness-eventos.md) | Arquitectura general, proveedores, contexto y recuperacion |
| [Incidente POS](incidentes/pos-planeacion-sin-ejecucion-2026-10-01.md) | Antes, causas, solucion y limites comprobados |
| [Coordinador por eventos](implementacion-coordinador-eventos-2026-09-30.md) | Flujos, contratos, criterios, controles y gaps pendientes |
| [Contexto y recursos](contexto-y-recursos.md) | Descubrimiento de rutas, cwd y comandos de sistema |
| [Contexto y memoria](contexto-y-memoria.md) | Recuerdos, sesiones, limites y antecedentes |
| [Diagnostico de no cierre](diagnostico-no-cierre.md) | Procedimiento actual para tarjetas, errores y acciones inciertas |
| [Inventario y limpieza](limpieza-datos-repo-2026-09-30.md) | Clasificacion, datos retirados y retencion |
| [Evidencias](evidence/README.md) | Procedencia y uso de registros versionados |

## Planes de implementacion

| Documento | Estado |
| --- | --- |
| [Goals verificables para proyectos y POS](plan-goals-proyectos-pos-2026-10-02.md) | Informe de la revision 5132301; ejercicio retirado del producto actual |

## Informes historicos

| Revision | Resultado documentado |
| --- | --- |
| [27 de septiembre: poema y proveedor](analisis-harness-poema-2026-09-27.md) | Fallo TokenRouter, defectos, migracion NVIDIA y poema posterior |
| [28 de septiembre: entradas por eventos](validacion-eventos-2026-09-28.md) | Migracion del CLI al reactor anterior y rutas de entrada |
| [30 de septiembre: auditoria previa](auditoria-gaps-harness-2026-09-30.md) | 18 candidatos previos a la sustitucion del coordinador |
| [Diagnostico del ejecutor anterior](historico/diagnostico-no-cierre-previo-2026-09-30.md) | Causas y commits del runner de pasos, conservados para consulta |

El manifiesto del 27 de septiembre es una foto historica. El inventario actual
se genera con [audit-repository.ps1](../scripts/audit-repository.ps1).
Las sondas antiguas requieren su revision de origen: no son especificacion
del actor nuevo ni deben sobrescribir la evidencia original.
