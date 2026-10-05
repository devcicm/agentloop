# Evidencia de separacion Core/Kernel

Fecha: 3 de octubre de 2026. Base: 5132301; rama feat/harness-eventos-tarjetas.

Antes: ejercicio POS versionado, protocolo en prompt, validador e imagen fijos,
DTOs compilados por el ejecutable y contexto sin registro durable de lecturas/notas.
Despues: tres proyectos .NET 9; Kernel sin ProjectReferences, Core referencia
Kernel y el host compone ambos. Perfil explicito y registros de adaptadores /
validadores, comandos configurados, hash de perfil y contexto durable por archivo.

[Compilacion](build.txt): solucion con 0 errores y 0 advertencias. Se adapto la
fixture existente a un ejemplo generico; no se agregaron ni ejecutaron pruebas
nuevas o inferencias remotas. El resultado de 415 checks de la revision anterior
no acredita el comportamiento de estos cambios. Docker generico queda compilado;
no se realizo una corrida de contenedor con un nuevo perfil en esta entrega.

[Inventario](repository-audit.json): clasificacion y enlaces de los archivos actuales.
[Retencion del ejercicio](exercise-retention.json): hashes y rutas locales.
Fuentes preservadas bajo artifacts/exercises/pos-legacy/, ignorado
por Git. Evidencia previa conservada en ../2026-10-03-proyectos/. La carpeta nueva
no reescribe sus registros. El commit final identifica esta entrega en Git.

Limitaciones: estimador UTF-8 aproximado y checkpoint derivado sin lectura
optimizada. No se mide aun el impacto remoto de notas/paginacion. El ejemplo de
archivos no certifica funcionalidad de aplicaciones. La plantilla Docker necesita
valores y assets externos reales; no tiene imagen o validador preparado por defecto.

[Arquitectura y migracion](../../architecture/core-kernel.md).
