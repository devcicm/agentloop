# Indice de documentacion

Documentacion actualizada al 1 de octubre de 2026; la ultima validacion operativa
registrada corresponde al 30 de septiembre. El codigo determina el comportamiento;
el [README del proyecto](../README.md) resume las entradas. Las guias describen
el uso vigente. Los informes fechados conservan observaciones de su revision.

## Guias vigentes

| Documento | Alcance |
| --- | --- |
| [Coordinador por eventos](implementacion-coordinador-eventos-2026-09-30.md) | Flujos, contratos, criterios, controles y gaps pendientes |
| [Contexto y recursos](contexto-y-recursos.md) | Descubrimiento de rutas, cwd y comandos de sistema |
| [Contexto y memoria](contexto-y-memoria.md) | Recuerdos, sesiones, limites y antecedentes |
| [Diagnostico de no cierre](diagnostico-no-cierre.md) | Procedimiento actual para tarjetas, errores y acciones inciertas |
| [Inventario y limpieza](limpieza-datos-repo-2026-09-30.md) | Clasificacion, datos retirados y retencion |
| [Evidencias](evidence/README.md) | Procedencia y uso de registros versionados |

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
