# Indice de evidencia

Los archivos se conservan por procedencia y fecha. Los registros de pruebas
sin red y sondas scripted no acreditan una ejecucion remota. Los logs antiguos
no describen necesariamente el codigo actual. No se regeneran sobre el original.

## Capturas y alcance

- 2026-10-04, Core/Kernel: suite, memoria, reparacion por eventos, perfiles Docker,
  NVIDIA remoto y recuperacion. Ver [validacion](2026-10-04-validacion-core-kernel/README.md).

- 2026-10-02, proyectos P0: contratos y admision; compilacion sin errores ni
  advertencias, sin tests nuevos. Ver [evidencia](2026-10-02-proyectos-p0/README.md).

- 2026-10-01, seguridad: sanitizacion, validacion de DTOs y scope;
  382 comprobaciones correctas. Ver [evidencia](2026-10-01-seguridad/README.md).

- 2026-10-01: incidente POS, proveedores reorganizados, contexto y recuperacion;
  352 comprobaciones locales correctas. Ver [evidencia actual](2026-10-01/README.md).

- 2026-09-27: fallo TokenRouter, sondas deterministas, correcciones iniciales,
  catalogos, migracion NVIDIA, chat y poema posterior. Ver el [informe](../analisis-harness-poema-2026-09-27.md).
- 2026-09-28 y `eventos-final-2026-09-30.log`: entradas del CLI sobre el reactor
  anterior. Ver la [validacion historica](../validacion-eventos-2026-09-28.md).
- 2026-09-30: actor nuevo, compilacion, suite de 340 comprobaciones y prueba
  real NVIDIA. Ver la [implementacion](../implementacion-coordinador-eventos-2026-09-30.md).

El manifiesto del 27 describe los bytes capturados entonces: el informe enlazado
ha recibido aclaraciones posteriores, por lo que su hash actual cambia. La
captura `after-fix-probe.json` contenia consola y se proceso como JSON valido;
[after-fix-probe-console.txt](2026-09-27/after-fix-probe-console.txt) conserva
exactamente sus bytes originales y el hash del manifiesto historico. No se
reescribe esa foto. El inventario vigente se produce con
[audit-repository.ps1](../../scripts/audit-repository.ps1).
`before-fix.patch` tiene cero bytes: registra que no habia diff en esa captura.
La sonda y `run-probe.ps1` son historicos; el script puede sobrescribir su salida
si se ejecuta. Para el actor vigente usar `selftest-coordinator`.

## Archivos procesados

| Archivo | Clasificacion |
| --- | --- |
| [2026-09-27/after-fix-probe-console.txt](2026-09-27/after-fix-probe-console.txt) | Captura original preservada; el JSON se extrajo sin repetir la sonda |
| [2026-09-27/HarnessProbe.cs.txt](2026-09-27/HarnessProbe.cs.txt) | Sonda historica: requiere revision de origen |
| [2026-09-27/after-fix-build.txt](2026-09-27/after-fix-build.txt) | Registro fechado de compilacion, prueba o estado |
| [2026-09-27/after-fix-probe.json](2026-09-27/after-fix-probe.json) | Resultado determinista sin proveedor remoto |
| [2026-09-27/after-fix-selftest.txt](2026-09-27/after-fix-selftest.txt) | Registro fechado de compilacion, prueba o estado |
| [2026-09-27/audit-summary.json](2026-09-27/audit-summary.json) | Registro fechado de compilacion, prueba o estado |
| [2026-09-27/before-fix-commit.txt](2026-09-27/before-fix-commit.txt) | Estado previo capturado |
| [2026-09-27/before-fix-status.txt](2026-09-27/before-fix-status.txt) | Estado previo capturado |
| [2026-09-27/before-fix.patch](2026-09-27/before-fix.patch) | Estado previo capturado |
| [2026-09-27/build.txt](2026-09-27/build.txt) | Registro fechado de compilacion, prueba o estado |
| [2026-09-27/chat-nvidia-console.txt](2026-09-27/chat-nvidia-console.txt) | Registro del proveedor NVIDIA en esa fecha |
| [2026-09-27/evidence-manifest.json](2026-09-27/evidence-manifest.json) | Manifiesto historico, no inventario actual |
| [2026-09-27/harness-probe.json](2026-09-27/harness-probe.json) | Resultado determinista sin proveedor remoto |
| [2026-09-27/interactive-memo.txt](2026-09-27/interactive-memo.txt) | Registro fechado de compilacion, prueba o estado |
| [2026-09-27/nvidia-catalog-after-fix.json](2026-09-27/nvidia-catalog-after-fix.json) | Catalogo observado en esa fecha, no disponibilidad vigente |
| [2026-09-27/nvidia-ping-after-fix.json](2026-09-27/nvidia-ping-after-fix.json) | Registro del proveedor NVIDIA en esa fecha |
| [2026-09-27/nvidia-poem-evidence.json](2026-09-27/nvidia-poem-evidence.json) | Artefacto o evidencia del poema de su captura |
| [2026-09-27/nvidia-poem-run.txt](2026-09-27/nvidia-poem-run.txt) | Registro del proveedor NVIDIA en esa fecha |
| [2026-09-27/nvidia-poem-workspace/notes/poema-original.md](2026-09-27/nvidia-poem-workspace/notes/poema-original.md) | Artefacto o evidencia del poema de su captura |
| [2026-09-27/provider-catalog.json](2026-09-27/provider-catalog.json) | Catalogo observado en esa fecha, no disponibilidad vigente |
| [2026-09-27/remote-goal.txt](2026-09-27/remote-goal.txt) | Registro fechado de compilacion, prueba o estado |
| [2026-09-27/run-probe.ps1](2026-09-27/run-probe.ps1) | Sonda historica: requiere revision de origen |
| [2026-09-27/selftest.txt](2026-09-27/selftest.txt) | Registro fechado de compilacion, prueba o estado |
| [2026-09-30/compilacion.txt](2026-09-30/compilacion.txt) | Registro fechado de compilacion, prueba o estado |
| [2026-09-30/inspeccion-journal.txt](2026-09-30/inspeccion-journal.txt) | Inspeccion de acciones/eventos sin repetir efectos |
| [2026-09-30/nvidia-poema-eventos.jsonl](2026-09-30/nvidia-poema-eventos.jsonl) | Artefacto o evidencia del poema de su captura |
| [2026-09-30/nvidia-poema-traza.txt](2026-09-30/nvidia-poema-traza.txt) | Artefacto o evidencia del poema de su captura |
| [2026-09-30/poema.txt](2026-09-30/poema.txt) | Artefacto o evidencia del poema de su captura |
| [2026-09-30/selftest-completo.txt](2026-09-30/selftest-completo.txt) | Registro fechado de compilacion, prueba o estado |
| [eventos-antes-2026-09-28.log](eventos-antes-2026-09-28.log) | Registro fechado de compilacion, prueba o estado |
| [eventos-despues-2026-09-28.log](eventos-despues-2026-09-28.log) | Registro fechado de compilacion, prueba o estado |
| [eventos-final-2026-09-30.log](eventos-final-2026-09-30.log) | Registro fechado de compilacion, prueba o estado |
| [eventos-nvidia-2026-09-28.log](eventos-nvidia-2026-09-28.log) | Registro del proveedor NVIDIA en esa fecha |
| [eventos-regresion-antes-2026-09-28.log](eventos-regresion-antes-2026-09-28.log) | Registro fechado de compilacion, prueba o estado |
| [eventos-regresion-despues-2026-09-28.log](eventos-regresion-despues-2026-09-28.log) | Registro fechado de compilacion, prueba o estado |
| [2026-10-01/pos-before.json](2026-10-01/pos-before.json) | Extracto del incidente y hashes de fuentes locales |
| [2026-10-01/build.txt](2026-10-01/build.txt) | Compilacion final sin errores ni advertencias |
| [2026-10-01/selftest.txt](2026-10-01/selftest.txt) | Suite final: 352 comprobaciones correctas |
| [2026-10-01/selftest-first-attempt.txt](2026-10-01/selftest-first-attempt.txt) | Intento inicial fallido y conservado |
| [2026-10-01/selftest-second-attempt.txt](2026-10-01/selftest-second-attempt.txt) | Intento de integracion fallido y conservado |
| [2026-10-01/repository-audit.json](2026-10-01/repository-audit.json) | Inventario de la entrega anterior al commit |
| [2026-10-01/README.md](2026-10-01/README.md) | Alcance de la validacion local |
| [2026-10-01-seguridad/README.md](2026-10-01-seguridad/README.md) | Alcance de pruebas de datos y seguridad |
| [2026-10-01-seguridad/build.txt](2026-10-01-seguridad/build.txt) | Compilacion sin errores ni advertencias |
| [2026-10-01-seguridad/selftest.txt](2026-10-01-seguridad/selftest.txt) | 382 comprobaciones locales correctas |
| [2026-10-01-seguridad/repository-audit.json](2026-10-01-seguridad/repository-audit.json) | Inventario documental de la entrega |
| [2026-10-02-proyectos-p0/README.md](2026-10-02-proyectos-p0/README.md) | Alcance de compilacion P0; sin comprobacion funcional |
| [2026-10-02-proyectos-p0/build.txt](2026-10-02-proyectos-p0/build.txt) | Compilacion sin errores ni advertencias |

## 3 de octubre de 2026: proyectos por eventos

[Indice y procedencia](2026-10-03-proyectos/README.md): suite local, aislamiento,
fixture POS con API/navegador y corrida NVIDIA. Los journals y evidencias completos
permanecen en almacenamiento local ignorado; se versionan resumenes sin claves.

## Separacion Core/Kernel y perfiles externos

[Entrega posterior del 3 de octubre](2026-10-03-core-kernel/README.md): compilacion,
inventario y migracion del ejercicio. La evidencia POS previa pertenece a 5132301.
