# Indice de documentacion

## Empezar aqui

1. [Proposito, alcance y limites del repo](proposito-y-alcance.md).
2. [Diagramas de flujo y responsabilidades](architecture/flujos-operativos.md).
3. [Arranque y capacidades actuales](../README.md).
4. [Plan de publicacion en GitHub](plans/publicacion-github-2026-10-07.md).
5. [Instalacion desde fuentes o ZIP local](guides/instalacion.md).

[Reorganizacion aplicada: estructura, nombres, dependencias y validacion](architecture/reorganizacion-implementada.md).


Documentacion actualizada al 4 de octubre de 2026: [Core/Kernel y perfiles externos](architecture/core-kernel.md).
[Compilacion de separacion](evidence/2026-10-03-core-kernel/README.md). Las 415 comprobaciones
y el POS anteriores corresponden a 5132301.
Validacion posterior: [suite e inferencia NVIDIA con Docker](evidence/2026-10-04-validacion-core-kernel/README.md).
[Auditoria de credenciales y almacenes](evidence/2026-10-04-credenciales/README.md).
Los informes fechados conservan su revision de origen.

[Formato de la consola: colores, sombreado y opciones](guides/consola-ui.md).

## Guias vigentes

| Documento | Alcance |
| --- | --- |
| [Comandos, permisos y edicion](guides/comandos-permisos-edicion.md) | Here-strings, aprobaciones por eventos, permisos continuos y lectura obligatoria |
| [Core/Kernel y host](architecture/core-kernel.md) | Bibliotecas independientes, puertos, perfiles y contexto |
| [Goals de proyectos por eventos](architecture/goals-proyectos.md) | Contratos, tareas, validadores, aislamiento, contexto y recuperacion |
| [Seguridad, datos y DTOs](guides/seguridad-datos-harness.md) | Sanitizacion, contratos, limites de confianza y uso acotado |
| [Harness vigente y flujos ASCII](architecture/harness-eventos.md) | Arquitectura general, proveedores, contexto y recuperacion |
| [Incidente POS](incidents/pos-planeacion-sin-ejecucion-2026-10-01.md) | Antes, causas, solucion y limites comprobados |
| [Coordinador por eventos](architecture/implementacion-coordinador-eventos-2026-09-30.md) | Flujos, contratos, criterios, controles y gaps pendientes |
| [Contexto y recursos](guides/contexto-y-recursos.md) | Descubrimiento de rutas, cwd y comandos de sistema |
| [Contexto y memoria](guides/contexto-y-memoria.md) | Recuerdos, sesiones, limites y antecedentes |
| [Diagnostico de no cierre](guides/diagnostico-no-cierre.md) | Procedimiento actual para tarjetas, errores y acciones inciertas |
| [Inventario y limpieza](archive/limpieza-datos-repo-2026-09-30.md) | Clasificacion, datos retirados y retencion |
| [Evidencias](evidence/README.md) | Procedencia y uso de registros versionados |

## Planes de implementacion

| Documento | Estado |
| --- | --- |
| [Publicacion en GitHub](plans/publicacion-github-2026-10-07.md) | Plan de primera prerelease, gaps, CI, empaquetado y criterios de salida; sin publicacion |
| [Goals verificables para proyectos y POS](plans/plan-goals-proyectos-pos-2026-10-02.md) | Informe de la revision 5132301; ejercicio retirado del producto actual |

## Informes historicos

| Revision | Resultado documentado |
| --- | --- |
| [27 de septiembre: poema y proveedor](archive/analisis-harness-poema-2026-09-27.md) | Fallo TokenRouter, defectos, migracion NVIDIA y poema posterior |
| [28 de septiembre: entradas por eventos](archive/validacion-eventos-2026-09-28.md) | Migracion del CLI al reactor anterior y rutas de entrada |
| [30 de septiembre: auditoria previa](archive/auditoria-gaps-harness-2026-09-30.md) | 18 candidatos previos a la sustitucion del coordinador |
| [Diagnostico del ejecutor anterior](archive/diagnostico-no-cierre-previo-2026-09-30.md) | Causas y commits del runner de pasos, conservados para consulta |

El manifiesto del 27 de septiembre es una foto historica. El inventario actual
se genera con [audit-repository.ps1](../scripts/audit-repository.ps1).
Las sondas antiguas requieren su revision de origen: no son especificacion
del actor nuevo ni deben sobrescribir la evidencia original.

- [Wizard de credenciales](guides/setup-credenciales.md).
- [Pendientes agenticos y prioridades](plans/mejoras-agenticas-2026-10-07.md).

- [Resultados, errores y observabilidad](guides/resultados-observabilidad.md).
