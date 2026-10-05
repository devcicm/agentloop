# Evidencia del 1 de octubre de 2026

Entrega sobre base 6d0e954, rama feat/harness-eventos-tarjetas.

| Archivo | Procedencia y resultado |
| --- | --- |
| [pos-before.json](pos-before.json) | Extracto local del incidente, fuentes y SHA-256; sin chats completos |
| [build.txt](build.txt) | dotnet build --no-restore: 0 errores, 0 advertencias |
| [selftest.txt](selftest.txt) | dotnet run --no-build -- selftest: 352 comprobaciones correctas, 38 del coordinador; SELF-TEST OK |
| [selftest-first-attempt.txt](selftest-first-attempt.txt) | Intento fallido: render de plantilla antes de definir variables; corregido el orden |
| [selftest-second-attempt.txt](selftest-second-attempt.txt) | Intento fallido: prueba anterior esperaba todo el historial de asistentes; actualizada al contrato de requisitos del usuario |
| [repository-audit.json](repository-audit.json) | Inventario del arbol de trabajo de esta entrega; SourceCommit es la base anterior al commit |

Los logs de prueba no usan credenciales ni red. El cliente NVIDIA se ejercita con
un handler HTTP simulado y framing SSE. El fixture de recuperacion escribe un
archivo temporal, comprueba un receipt real y cierra. No constituye un POS completo.

La suite final valida ademas el seguimiento simple/acentuado: se corrigio el
escape de fronteras de palabra en la expresion de reconocimiento. Los intentos
fallidos se conservan para dejar trazabilidad de la integracion.

Las rutas temporales de los logs son ubicaciones de fixtures; no se promete su
retencion permanente. La evidencia original POS permanece en .agentloop ignorado;
el extracto permite identificarla por hash. No hubo nueva inferencia NVIDIA.

Descripcion: [incidente](../../incidents/pos-planeacion-sin-ejecucion-2026-10-01.md)
y [arquitectura vigente](../../architecture/harness-eventos.md).
