# Evidencia de reorganizacion

Base Git: c196ffb; rama de implementacion: feat/estructura-core-harness.
[Arquitectura, antes/despues y nombres](../../architecture/reorganizacion-implementada.md).

- Compilacion Debug y Release: 0 errores, 0 advertencias.
- Suite: 442 comprobaciones, incluidas 48 de proyectos y 6 nuevas de contexto/adaptadores.
- Fixture por CLI: Succeeded, run 0fec46c3d68849e2b861e71dda4dc481.
- NVIDIA GLM 5.3: Succeeded, run cd5267b990ff4f239a5acc04d9cb76a7.
- Entregables externos: modulo ESM add(a,b) y README; validacion de archivos y cuatro casos numericos en Docker.
- Journal nuevo: terminal=true, uncertainActions=[]; journal anterior tambien interpretable.
- F5: perfiles, directorio de trabajo e identidad del proyecto de inicio conservados; arranque comprobado por consola. No se automatizo Visual Studio.

El primer preflight remoto rechazo el hash CRLF del perfil historico ante el recurso versionado LF.
Esta ejecucion usa un perfil nuevo con hashes actuales. La captura anterior conserva sus bytes y valores.
Una compilacion Debug concurrente con el remoto encontro DLLs bloqueadas; se repitio correctamente al terminar.
El CLI selftest ejecuta el verificador compilado con --no-build, evitando recompilar el host abierto.

Los informes de auditoria y el manifiesto de esta carpeta completan la procedencia.
Los archivos bajo generated-example son copias de evidencia; el proyecto ejecutado vive fuera del repo.
El resultado acredita los validadores instalados, sin afirmar la correccion de sistemas de negocio completos.
