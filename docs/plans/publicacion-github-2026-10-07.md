# Plan de publicacion de AgentLoop en GitHub

Fecha: 2026-10-07. Revision inspeccionada: `544a647`.
Estado: plan; no se crearon remotos, repositorios, tags ni releases.

## 1. Estado observado

| Elemento | Resultado |
| --- | --- |
| GitHub CLI | Cuenta `devcicm` activa, HTTPS, token en keyring |
| Permisos del token | `repo`, `workflow`, `read:org`, `gist`; no se copio al proyecto |
| Remoto del repo | No hay remoto configurado |
| Rama actual | `feat/estructura-core-harness`, HEAD `544a647` |
| `main` | `c196ffb`; cinco commits pendientes de integrar |
| Tags | Ninguno |
| Arbol local | `ping_google.ps1` no versionado; no incluirlo por accidente |
| Arquitectura | Kernel, Core, Harness, Infrastructure, Compatibility, CLI y verificador separados |
| Target | Los siete proyectos usan `net9.0` |
| Version de producto | Sin version explicita en csproj ni archivo central |
| CI y release | No existe `.github/workflows/` |
| Documentos de distribucion | Faltan LICENSE, CHANGELOG, CONTRIBUTING y SECURITY |
| Secretos | `local-secrets/**`, `.env*` y `.agentloop/` excluidos; solo README de secretos versionado |
| Evidencia | Auditoria de secretos del 4 de octubre; 442 comprobaciones historicas de la reorganizacion |
| Cambios del 5 de octubre | Compilacion documentada; sin pruebas funcionales de permisos, editor y nueva ruta de chat |

`gh repo list` devolvio, entre candidatos con nombres relacionados, `devcicm/Agente`
y `devcicm/crypto-trading-agent`, ambos privados. No hay evidencia de que alguno
sea el destino de este repo; no conectarlos sin decidirlo.
El token autenticado no demuestra permisos administrativos en un destino aun no elegido.

## 2. Publicacion propuesta

- Destino provisional: nuevo `devcicm/agentloop`, privado inicialmente.
- Primera entrega propuesta: `v0.1.0-alpha.1`, marcada como prerelease.
- Producto inicial: CLI Windows x64 en ZIP autocontenido y codigo fuente.
- Core/Kernel siguen siendo bibliotecas separadas; publicar NuGet es una fase posterior.
- No prometer soporte Linux/macOS hasta validar esas plataformas.
- Licencia y visibilidad definitiva: decisiones del propietario; no inventar una licencia.

GitHub vincula releases a tags y permite adjuntar binarios y notas. Una release
debe apuntar al commit que genero y valido los archivos, no al HEAD cambiante.
[Referencia de GitHub](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

El proyecto usa .NET 9, cuyo soporte finaliza el 10 de noviembre de 2026.
Conviene migrar a .NET 10 LTS antes de una entrega estable; una preview en .NET 9
requiere declarar ese plazo y fijar una fecha de migracion. .NET 10 tiene soporte
hasta el 14 de noviembre de 2028, segun la
[politica oficial de Microsoft](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).
La compatibilidad con el Windows objetivo debe comprobarse antes de elegir SDK/runtime.

## 3. Gaps y prioridades

Las prioridades siguientes son riesgos de entrega, no afirmaciones de incidentes nuevos.

| Prioridad | Gap observado o pendiente | Mejora y condicion de salida |
| --- | --- | --- |
| Critica, puerta de publicacion | La auditoria anterior no acredita el commit de release ni los ZIP nuevos | Auditar historial, refs a publicar y archivos empaquetados; cero candidatos reales sin resolver; rotar cualquier secreto expuesto |
| Alta | Sin destino/remoto, main atrasada y archivo local suelto | Elegir destino; clasificar el script; integrar cinco commits tras validar; arbol de entrega limpio |
| Alta | Cambios recientes solo compilados | Regresion funcional de comandos, permisos, lectura previa, chat por eventos y recuperacion |
| Alta | Sin CI reproducible ni SDK fijado | `global.json`, configuracion comun y workflow local sin token NVIDIA |
| Alta | Binario sin version de producto ni paquete definido | Version desde fuente central, ZIP, SHA-256 y manifiesto con commit/RID/SDK |
| Alta para publicacion abierta | Sin licencia ni reglas de contribucion/seguridad | Elegir licencia; agregar LICENSE, CONTRIBUTING y SECURITY; revisar titularidad de archivos |
| Alta | `selftest` busca el arbol tests y ejecuta `dotnet run --no-build` | Mantenerlo como comando de desarrollo documentado o distribuir verificador separado; ZIP no debe anunciar capacidad inexistente |
| Alta | Datos/memoria/evidencia dependen de `SecretStore.ResolveRepositoryRoot()` | Separar workspace, almacen de credenciales y directorio de datos mediante configuracion y puertos |
| Alta antes de estable | .NET 9 cerca del fin de soporte | Migracion coordinada a .NET 10, sin mezclar runtimes por biblioteca |
| Media | README mezcla estado actual con evidencia de revisiones anteriores | Resumen de capacidades actual; evidencias con commit y alcance; changelog de primera release |
| Media | 131 archivos de evidencia versionados y rutas locales en informes | Revisar datos personales, ruido y ejemplos; conservar procedencia sin publicar capturas privadas |
| Media | Auto con shell y clasificacion por patrones | Explicar limites; instalacion inicial con modo confirmar; conservar permisos por eventos, nunca concedidos por el modelo |
| Media | Sin automatizacion de distribucion | Script de empaquetado explicito, seleccion de archivos, checksums y lista de exclusiones |
| Baja | Falta experiencia de contribucion y soporte | Plantillas de issue/PR, instrucciones de reproduccion y enlaces desde README |

El clasificador de comandos no es un sandbox. El editor tipado exige recibos;
un shell arbitrario puede escribir por otras vias. Estas limitaciones deben
aparecer en las notas de release y guia de inicio.

## 4. Fases y entregables

### Fase A: preparar el candidato local

1. Resolver nombre, visibilidad, titular/licencia y sistemas soportados.
2. Mantener `ping_google.ps1` fuera del candidato o moverlo conscientemente a un
   ejemplo revisado. Su contenido actual usa `Invoke-Expression`; no es necesario
   convertirlo en herramienta del producto ni incluirlo en la release.
3. Crear rama `release/0.1.0-alpha.1` desde la rama de trabajo actual.
4. Agregar version central y `global.json` con un SDK compatible instalado.
5. Decidir migracion a .NET 10 antes de congelar el candidato o limitar la preview.
6. Preparar LICENSE cuando se elija, CHANGELOG, SECURITY, CONTRIBUTING y guia de instalacion.
7. Renovar auditoria de secretos. No reescribir historial ni borrar evidencia si
   no existe un motivo confirmado. Revisar tambien datos personales sin tokens.

Salida: candidato identificado por commit, sin contenido local accidental y con
limites de uso/documentacion coherentes. No hacer push hasta completar la revision.

### Fase B: validar y construir en limpio

Comandos previstos, todavia no ejecutados como parte de este plan:

```powershell
dotnet restore AgentLoop.sln
dotnet build AgentLoop.sln -c Release --no-restore
dotnet run --project tests/AgentLoop.SelfTests -c Release --no-build -- selftest
python scripts/audit-architecture.py
python scripts/audit-git-secrets.py --output .agentloop/release-secrets.json
```

El verificador es un ejecutable propio: `dotnet test` por si solo no sustituye
su suite. Revisar sus efectos y emplear temporales/workspaces aislados en CI.

| Area | Casos exigidos |
| --- | --- |
| Entrada | chat/run/texto libre interactivo usan reactor; run-chat queda separado; catalogo mostrado correcto |
| Terminal | Here-string con saltos reales, rutas con espacios, resultado observado y timeout |
| Permisos | Destructivo/opaco pregunta; rechazo no inicia proceso; continuo acotado; revocacion; ID duplicado/incorrecto; cancelacion/deadline mientras espera |
| Documentos | Sin lectura rechaza; rango no leido rechaza; hash cambiado rechaza; texto/palabra/lineas/parrafo; coincidencias ambiguas; paginacion; UTF-8/UTF-16, BOM y CRLF |
| Preview | No escribe ni genera evidencia de efecto; escritura invalida recibos; error posterior exige reconciliacion |
| Eventos | Pausa/reanudacion, salida/fin, cierre con evidencia, sin acciones posteriores al cierre, recuperacion sin repetir efectos inciertos |
| Distribucion | ZIP extraido fuera del repo, ruta con espacios, cwd distinto, ausencia de secrets/tests; ayuda y configuracion comprensibles |
| Proveedor | Una prueba NVIDIA controlada y separada; capacidad/modelo constatados, sin asumir disponibilidad por aparecer en catalogo |

Las pruebas remotas requieren secreto dedicado y autorizacion para consumo;
no deben ejecutarse en PR de terceros ni ser requisito de cada build local.

### Fase C: empaquetar

Propuesta inicial: publicar el proyecto CLI, no todos los proyectos de la solucion.
Autocontenido, sin trimming ni single-file en la primera entrega hasta validar
reflexion, descubrimiento de archivos y carga de configuracion.

```powershell
dotnet publish src/AgentLoop.Cli/AgentLoop.Cli.csproj -c Release -r win-x64 --self-contained true -o .agentloop/release/win-x64
```

Version, commit y SDK deben inyectarse desde la configuracion de release,
sin IDs/modelos/proyectos de negocio incrustados en el kernel.

Archivos propuestos:

```text
AgentLoop-v0.1.0-alpha.1-win-x64.zip
SHA256SUMS.txt
release-manifest.json
release-notes.md
```

ZIP: binarios del publish, instrucciones, licencia elegida y ejemplos declarativos
sin datos privados. No copiar el workspace entero. No incluir `.git`, `.vs`,
`.agentloop`, `local-secrets` con claves, sesiones, logs, perfiles de evidencia
local ni el token del CLI. El manifiesto debe indicar tag, commit, SDK, RID,
modo autocontenido, pruebas realizadas y limitaciones. Firmas de codigo o
atestaciones son mejoras independientes; un checksum no acredita al autor.

### Fase D: conectar GitHub y activar CI

1. Confirmar destino; si ya existe, revisar su historial antes de conectar origin.
2. Crear repo solo si corresponde; conectar origin y comprobar identidad.
3. Subir ramas seleccionadas de forma explicita; evitar `--mirror` o `--all`.
4. Abrir PR hacia main y revisar diff, historial, workflows y datos publicados.
5. Configurar proteccion/ruleset segun capacidades del plan/visibilidad;
   impedir force push y exigir checks cuando esten disponibles.
6. CI de PR: build Release, suite local, arquitectura, secretos y empaquetado de prueba.
7. Workflow de release manual con version/commit concretos; generar borrador
   prerelease, comprobar sus archivos y publicar el borrador aprobado.

CI usa `GITHUB_TOKEN` de Actions, no el token personal del keyring.
Permisos `contents: read` para validacion; `contents: write` solo en el trabajo
de release. Fijar acciones por SHA revisado, limitar concurrencia y no exponer
secretos a codigo de PR no confiable.
[Practicas oficiales de Actions](https://docs.github.com/en/actions/reference/security/secure-use).

Secret scanning y push protection dependen del tipo de repo/plan. Comprobar su
disponibilidad; mantener el escaneo local aunque existan controles de GitHub.
[Alcance oficial de secret scanning](https://docs.github.com/en/code-security/concepts/secret-security/secret-scanning).

### Fase E: tag y prerelease

1. Integrar el candidato validado en main y confirmar su SHA.
2. Tag anotado `v0.1.0-alpha.1` en ese SHA; comprobar que no exista ni se mueva.
3. Subir el tag concreto, construir/validar sus assets y generar borrador.
4. Revisar notas, hashes, manifiesto, ausencia de secretos y arranque fuera del repo.
5. Publicar prerelease y verificar descarga desde GitHub.
6. Un defecto posterior genera nueva version; no sustituir silenciosamente tag/assets.
   Si hay filtracion: revocar/rotar, retirar assets afectados y seguir SECURITY.

## 5. Flujo y estructura previstos

```text
Estado actual
  main c196ffb
       |
       +--> feat/estructura-core-harness 544a647 (+5 commits)
                    |
                    +--> codigo local, sin origin, sin tags, sin CI

Flujo propuesto
  rama actual --> release/0.1.0-alpha.1
                        |
                        v
             version + docs + auditoria + pruebas en limpio
                        |
                        v
             destino confirmado --> origin --> PR --> CI --> main
                                                               |
                                                               v
                         tag fijo --> publish --> ZIP/hashes/manifiesto
                                                               |
                                                               v
                                 borrador prerelease --> revision --> publicar
```

Archivos nuevos propuestos, todavia no implementados:

```text
/
|-- global.json                         SDK reproducible
|-- Directory.Build.props               version y propiedades compartidas
|-- LICENSE                             licencia elegida por titular
|-- CHANGELOG.md                        cambios por version
|-- CONTRIBUTING.md                     flujo de contribucion
|-- SECURITY.md                         reporte de vulnerabilidades
|-- .github/
|   |-- workflows/ci.yml                validacion local sin LLM remoto
|   |-- workflows/release.yml           build y borrador desde tag/commit
|   `-- pull_request_template.md        evidencia y alcance del cambio
|-- scripts/package-release.ps1         seleccion de assets y SHA-256
`-- docs/guides/instalacion.md           uso desde ZIP fuera del repo
```

## 6. Criterio para aprobar la primera publicacion

### Documentacion de la entrega

Base documental creada: [proposito y alcance](../proposito-y-alcance.md),
[flujos ASCII actuales](../architecture/flujos-operativos.md), README e indice.
Antes de publicar, revisar que reflejen el commit del tag. El flujo de release
se identifica como propuesto y no como funcionalidad ya publicada.

| Documento | Contenido requerido |
| --- | --- |
| README | Que es, para quien, requisitos, arranque, capacidades y enlaces |
| Proposito/alcance | Harness reutilizable, independencia Core/Kernel, dominio externo, limites |
| Diagramas | Dependencias, eventos, permisos, archivos, proyecto/recuperacion y release |
| Instalacion | ZIP fuera del repo, configuracion, credenciales dedicadas y diagnostico |
| CHANGELOG/notas | Cambios, compatibilidad, limitaciones y evidencia del candidato |
| SECURITY/CONTRIBUTING/LICENSE | Seguridad, colaboracion y licencia elegida |

La guia de instalacion del ZIP y los documentos de distribucion pendientes se
completan al preparar el paquete; no prometer comandos o plataformas no validados.

- Destino, visibilidad, licencia y alcance de plataformas resueltos.
- Commit/tag/version concuerdan; main incorpora el candidato validado.
- SDK fijado; soporte de runtime documentado y migracion decidida.
- Suite actual, permisos/editor/chat/eventos y paquete validados con evidencia nueva.
- Secretos e informacion privada revisados en historial y assets finales.
- CLI funciona fuera del repo; comandos de desarrollo claramente separados.
- CI y borrador de release reproducen el mismo commit y hashes verificables.

## 7. Mejoras posteriores a la preview

1. Separar configuracion de workspace, datos y credenciales; admitir host instalado.
2. Reducir divergencias de entradas CLI sin volver a turnos como eje del harness.
3. Formalizar permisos/effect metadata por contratos de tools, sin confiar solo en nombres.
4. Validar instalacion Linux/macOS y empaquetar cada RID demostrado.
5. Publicar Core/Kernel en NuGet con version y politica de compatibilidad de DTOs/eventos.
6. Ampliar recuperacion, presion de eventos y limites de memoria con pruebas reproducibles.
7. Evaluar firma de binarios/atestacion de assets y actualizacion del runtime autocontenido.

Este plan se basa en inspeccion de Git, fuentes y evidencia existente. No se
ejecutaron pruebas, build, auditoria nueva de secretos ni publicacion en esta revision.
La autenticacion del CLI fue comprobada sin exponer el token.
