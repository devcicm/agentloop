# Evidencia: proyectos verificables por eventos

Fecha: 3 de octubre de 2026, America/Mexico_City. Rama:
`feat/harness-eventos-tarjetas`. Base previa: `81455f5` (P0).
Los registros corresponden a cambios locales sobre esa base; no a un commit
ya publicado. El commit final se identifica en Git.

## Antes

P0 admitia contratos y bloqueaba su ejecucion. El goal textual no tenia DAG de
entregables, oraculos POS, backend aislado ni reconstruccion de proyecto.
Docker estaba instalado, pero el motor Linux no estaba iniciado. Se inicio Docker
Desktop y se construyo la imagen usando **solo ProjectAssets como contexto**.
No se incluyeron secretos locales en el contexto de build.

## Resultados comprobados

| Evidencia | Resultado / limite |
| --- | --- |
| [build-final.txt](build-final.txt) | Compilacion actual: 0 errores, 0 advertencias |
| [selftest-final.txt](selftest-final.txt) | 415 comprobaciones correctas; 33 nuevas de proyectos, backend simulado |
| [backend-check.txt](backend-check.txt) | 8 checks de aislamiento reales, codigo 0 y limpieza confirmada |
| [fixture-final.txt](fixture-final.txt) | Succeeded: 4 tareas, 10 archivos, validacion final integrada |
| [validation-summary.json](validation-summary.json) | Resultados tipados, inputs/hash, ambiente, tiempos y hashes de fuentes locales |
| [nvidia-pos.txt](nvidia-pos.txt) | Primera corrida GLM: Failed, 60 llamadas; archivos reales, compilacion fallida |
| [nvidia-pos-resume.txt](nvidia-pos-resume.txt) | Recuperacion con presupuesto contractual original; resultado final indicado abajo |

La suite se cuenta por lineas `[ok]` y `OK project:`. No suma mensajes de
estado ni supone que pruebas simuladas acreditan aislamiento real.

### Fixture real

Run final: `72ccd2c4-c211-4300-91ff-c65d052bce1f`.
Root: `artifacts/generated/pos-verificado-2026-10-03`.
Contrato: `530D28C869A634A5872DF3796ABC6134FA7CB4840973253699959B36E07F80FA`.
Cliente **fixture-pos**, determinista, sin NVIDIA. Usa las herramientas, tarjetas
y actor existentes; build/API/navegador se ejecutan en Docker real.
14 resultados: siete de tareas y siete de integracion, todos correctos.
Seis procesos aislados: build/health/pos en entrega y otra vez en integracion.
El oraculo POS registra health, catalog, atomic-rejection, insufficient-stock,
sale-ticket-change-stock, idempotency, restart-persistence, browser-sale y
delivery-instructions. BrowserVerified=true. Los procesos se retiran antes del cierre.

Primera corrida conservada en [fixture-first.txt](fixture-first.txt), run
`b31f53f2-13c7-48fd-8d8e-12fa9270261f`, tambien correcta. Su etiqueta de modelo
heredaba GLM aunque el comando usaba --fixture: **no fue inferencia remota**.
Se corrigio la etiqueta y se repitio sobre una raiz nueva, sin borrar entregables.

### Ambiente e incidencias de integracion

Imagen local fijada por ID:
`sha256:06b51b6ad458af6707f0e4cfff1b1cf1451ff395bf7f7bd370cb24fe6ad55b86`.
Node 24, TypeScript 5.7.3, Playwright 1.56.1. Oraculos host montados readonly;
su codigo se entrega fuera de la imagen, por lo que el ID solo identifica la imagen.

[Docker build](docker-build.txt): PowerShell marco progreso por stderr como
NativeCommandError y el wrapper retorno 1; exportacion de imagen, inspect y
corridas reales comprobaron que la construccion si termino. No se usa ese codigo
del wrapper como prueba de fallo del Docker build.
[build-retry.txt](build-retry.txt) conserva cinco advertencias de bloqueo de DLL
por el fixture aun activo; la compilacion termino sin errores. Se repitio cuando
termino y build-final no tiene advertencias.

La primera prueba local de Node detecto sintaxis de parameter properties no
admitida en strip-only: se cambio a asignacion explicita de campos. Otros errores
de integracion C# se corrigieron antes de la compilacion final. No se conservaron
logs separados para cada intento: no se afirma evidencia de archivo inexistente.

### NVIDIA y recuperacion

Primera corrida: `dc1e188c-03e4-4eb5-abe5-e62b236214f8`, z-ai/glm-5.3.
Contrato: `E117CA8E9BB10C9CA335CEB73E2FE89129A10F19EEB6D76684164307BAEB176F`.
Root: `artifacts/generated/pos-remoto-2026-10-03`.
60 requests, 14 llamadas de escritura y 22 de lectura. Tres tareas se aceptaron
con validadores de presencia; al cambiar inputs se invalidaron. La entrega
funcional fallo por tsconfig incompatible con imports .ts, tipos node:* ausentes
y export Caja inexistente. El host rechazo el cierre y termino por limite de
llamadas. Tener archivos presentes no certifica compilacion ni funcionalidad.

La recuperacion conserva deadline original, 60 llamadas consumidas y baseline;
se habilito hasta el maximo contractual de 120. No se copio el fixture sobre los
archivos remotos. Se incorporo project_evidence para consultar registros de hasta
4000 caracteres, porque brief/snapshot truncaban diagnosticos importantes.
Resultado de recuperacion: **Failed**, run
`bcf8b517-36ee-4147-9ec3-6f369515b5d6`. No escribio cambios: lecturas reiteradas,
una lista y una tarjeta; repitio ventas.ts, servidor.ts y package.json. El host
termino con `Sin progreso material tras 12 acciones; presupuesto de tarea agotado`.
El resumen conserva las llamadas acumuladas. El POS remoto **no es funcional
verificado**; permanece como evidencia de fallo, sin reemplazarlo por el fixture.

Esto deja un candidato de mejora de contexto: evitar que la poda y la relectura
de archivos grandes consuman el presupuesto sin producir una correccion. La
consulta de evidencia ya implementada da acceso al detalle, pero esta corrida
no la utilizo. No se demuestra que ese cambio resuelva por si solo la conducta
del modelo. Cambiar presupuestos o proveedor requiere una nueva evaluacion;
no se declara exito por aumentar limites.

## Procedencia y retencion

Los logs de consola se normalizan a UTF-8/LF al cerrar las corridas. Los journals
completos y registros privados permanecen bajo `.agentloop/`, y los journeys bajo
`artifacts/journeys/`; son datos operativos ignorados por Git. El resumen publico
conserva SHA-256 de journal y registros, sin publicar razonamiento completo ni claves.
Los logs intermedios [build.txt](build.txt) y [selftest.txt](selftest.txt) se conservan:
la suite intermedia tiene 412 checks; la final agrega tres controles de raiz/snapshot.

Los checks de atomicidad cubren los rechazos indicados, no todas las fallas posibles
de SQLite. El POS minimo no incluye fiscalidad, usuarios o despliegue. El backend
es offline y requiere toolchain/dependencias preparados. El checkpoint es derivado,
el journal autoritativo. Este perfil aislado no cambia los goals textuales generales.

[Guia, ASCII y recuperacion](../../goals-proyectos.md).

[Inventario final del repositorio](repository-audit.json): archivos versionables,
enlaces y JSON comprobados; sin errores ni duplicados exactos. Los datos operativos
ignorados se clasifican y conservan; las credenciales no se inspeccionan.
