# Evidencia: datos y seguridad del harness

Fecha: 1 de octubre de 2026. Base de trabajo: a28ccfb.

| Archivo | Resultado y procedencia |
| --- | --- |
| [build.txt](build.txt) | dotnet build --nologo --no-restore: 0 errores, 0 advertencias |
| [selftest.txt](selftest.txt) | dotnet run --no-build -- selftest: 382 comprobaciones correctas, 68 del coordinador; SELF-TEST OK |
| [repository-audit.json](repository-audit.json) | Inventario de la entrega previo al commit; SourceCommit identifica la base |

Incluye 30 comprobaciones nuevas de seguridad: duplicados/profundidad, tipos y
campos de argumentos, ausencia de efectos ante rechazo, recibos y metadatos
externos, vista acotada, conservacion de tipos JSON, controles Unicode, secretos
fragmentados en streaming, errores de proveedor, rutas protegidas/ADS, scope
sin actor, memoria duplicada y limites de bytes. Los fixtures generan sus datos
y archivos temporales: no se utilizan claves reales ni servicios remotos.

Las suites anteriores siguen pasando: cierre/evidencia, pausa/cancelacion,
aprobacion, simulacion, ausencia de sondeo de procesos silenciosos y causalidad.
Durante desarrollo se corrigio la declaracion required de JsonExtensionData,
que .NET rechaza, y se enlazaron restricciones a goal, journal y chat legacy.
Los registros aqui son la ultima ejecucion de la version final de codigo.

No es una prueba de inmunidad de un modelo remoto a prompt injection ni de
contencion del sistema operativo. Alcance y pendientes en
[seguridad de datos y DTOs](../../seguridad-datos-harness.md).
Los logs se normalizan a UTF-8/LF para versionado.
