# Datos, DTOs y seguridad del harness

## Ampliacion de permisos y archivos (2026-10-05)

Los comandos detectados como destructivos u opacos piden autorizacion puntual,
incluso en auto. El permiso continuo se limita a shell/cwd y memoria de sesion;
no modifica vetos. Las tools de edicion exigen lectura previa con hash/rango.
Ver [protocolo, flujos y limites](comandos-permisos-edicion.md).


Entrega: 1 de octubre de 2026. Base: a28ccfb; rama feat/harness-eventos-tarjetas.
La operacion sigue dirigida por eventos. Los controles se ejecutan al recibir
entrada, admitir acciones y registrar resultados; no agregan un ciclo de polling.

## Diagnostico de la estructura anterior

Ya existian DTOs de proveedor (ChatRequest, ChatMessage), resultados parseados,
AgentEvent, ToolExecution, recibos y JournalEntry. Eran una base util, pero
serializar un objeto no validaba su contenido ni su autoridad.

| Hueco observado | Cambio aplicado |
| --- | --- |
| Schemas publicados pero argumentos sin validacion completa | Validacion comun antes de InvokeAsync y antes de admision del actor |
| Diccionarios toleraban duplicados y campos imprevistos | StrictJson y additionalProperties=false por defecto |
| Arrays/numeros convertidos a strings en EnsureJsonObject | Conservacion de los tipos JSON y fallback valido para texto como 123abc |
| Resultado externo podia incluir receipt_id o _harness | Eliminacion de campos reservados; host emite recibo/metadatos |
| Salida de archivo/proceso mezclada con instrucciones | DTO de datos con source/trust, instrucciones fijas y hallazgos de inyeccion |
| Lectura HTTP/SSE sin presupuesto de bytes | Lectura acotada antes de materializar respuestas normales y errores |
| Secretos/controles podian llegar a consola o journal | Redaccion de formatos reconocibles y filtrado de controles/bidi |
| Herramientas podian tocar secretos y memoria interna | Recursos protegidos, rechazo de enlaces y alias Windows/ADS |

## Organizacion actual

```text
Security/
+-- Contracts/HarnessDataDtos.cs
|   +-- ToolResultDto + ToolDataMetadataDto
|   +-- DataFindingDto
|   +-- PromptDataDto
|   +-- ToolFailureDto
|   +-- ModelRequestDto
+-- DataLimits.cs              limites centralizados
+-- StrictJson.cs              duplicados, profundidad y limpieza estructurada
+-- ToolArgumentValidator.cs   schema del registro antes de ejecutar
+-- DataGuard.cs               entrada, datos al prompt y secretos
+-- ProtectedResources.cs      secretos, Git, memoria y aliases Windows
+-- BoundedReadStream.cs       presupuesto del transporte
+-- SafeStreamingText.cs       claves fragmentadas y buffer de consola
+-- SecuritySelfTest.cs        contratos locales, sin red
```

Los contratos de Chat permanecen en src/AgentLoop.Kernel/Llm. Los DTOs nuevos usan
nombres C# legibles; JsonPropertyName conserva campos snake_case del protocolo.
No es necesario crear un DTO distinto para cada payload de cada herramienta:
ToolResultDto tiene metadatos tipados y JsonExtensionData para datos heterogeneos.
Su esquema v1 conserva path/content/error/receipt_id en la raiz para compatibilidad.
El cuerpo variable se valida como JSON; el schema de argumentos es especifico de
cada herramienta. Los datos variables no se convierten en permisos del harness.

Ejemplo de mensaje role=tool:

```json
{
  "_harness": {
    "Version": 1,
    "Source": "read_text_file",
    "Trust": "untrusted_data",
    "Sha256": "HASH_DEL_RESULTADO_NORMALIZADO",
    "Truncated": false,
    "Findings": []
  },
  "path": "C:\\proyectos\\demo\\nota.txt",
  "content": "Contenido externo, incluso si contiene instrucciones",
  "receipt_id": "EMITIDO_POR_OPERATION_ACCEPTANCE"
}
```

_Harness es reservado (tambien sus variantes de mayusculas). Un receipt entrante
se elimina antes de observar el resultado. Solo OperationAcceptance puede emitir
uno para una herramienta exitosa de la transaccion actual. El hash del DTO es
una huella del resultado normalizado; no autentica al autor ni demuestra ausencia
de poisoning. Los hashes de artefactos siguen verificando archivos reales.

## Flujos implementados

```text
ENTRADA DEL OPERADOR
limite -> Plain/control/bidi -> redaccion conocida -> contrato de operacion
                                                        |
                                        referencia estructurada en prompt

PROPUESTA DEL MODELO (dato no confiable)
JSON unico -> limite/profundidad -> tool/ID -> schema/required/type/enum
                                                        |
                        scope + rutas + recursos protegidos + tarjetas
                                                        |
                               politica/no-shell -> efecto async
                                                        |
                                   evento de resultado

SALIDA DE TOOL / ARCHIVO / PROCESO
normalizar JSON -> limpiar strings -> quitar metadatos/recibos externos
                                                        |
                                   clasificar exito/fallo
                                                        |
                              recibo real y journal normalizado
                                                        |
                           ToolResultDto [source, trust, hash, findings]
                                                        |
                   recorte de vista al prompt -> nueva decision por evento

PROVEEDOR HTTP / SSE -> BoundedReadStream -> StrictJson -> parser
CONSOLA streaming -> buffer acotado -> filtrado/redaccion -> salida visible
```

No se ejecutan instrucciones del texto de una tool directamente. Las acciones
propuestas se validan en codigo aunque el modelo interprete mal ese texto.
La deteccion instruction_like_data es heuristica y se registra en Findings;
no elimina el contenido ni prueba que una entrada sea segura. Bloquear un efecto
corresponde a schema, scope, politica, tarjetas y evidencias, no a una regex de
lenguaje natural. Memoria cargada/recuerdos se presentan como referencia y no
pueden instalar politicas; se rechazan claves JSON duplicadas y roles invalidos
en sesiones. Las salidas no llaman a Remember ni alteran preferencias del host.

## Limites y comportamiento de datos

| Frontera | Limite actual |
| --- | --- |
| Entrada del operador | 32000 caracteres; exceso se rechaza |
| Argumentos de tools | 1100000 caracteres JSON; hasta 32 niveles |
| Salida de tools | 1100000 caracteres; estructura ambigua se rechaza |
| Proveedor | 2 MiB de bytes leidos por respuesta o stream completo |
| Tools por respuesta | 32 |
| Vista de resultado al prompt | 12000 caracteres de excerpt, mas metadatos |
| Buffer de una palabra en consola | 4096 caracteres; exceso se omite con marca |

El validador implementa el subconjunto usado por este repo: objeto, propiedades,
required, tipos, enteros de 32 bits, numeros finitos, enum, items, maxLength y
additionalProperties. No es un motor completo de JSON Schema. No publicar nuevos
keywords como garantias de validacion sin implementarlos antes. Texto normal se
envuelve; JSON que pretende ser estructurado pero es invalido/duplicado se rechaza.
SSE malformado no se ignora silenciosamente. Los IDs admiten letras ASCII,
numeros, guion, punto y subrayado, con maximo de 128 caracteres.

La limpieza afecta datos que viajan al prompt/registro/consola; no reescribe los
archivos leidos. Los argumentos de contenido para escribir codigo no se limpian
arbitrariamente: se validan estructura y limites. Claves reconocibles que pasan
por el cuerpo HTTP se redactan y no deben incrustarse en codigo generado.
La redaccion actual cubre nvapi-, sk- y Bearer reconocibles; no es DLP universal,
no cubre todos los secretos posibles ni codificaciones/fragmentaciones arbitrarias.
La consola guarda palabras para manejar nvapi/sk partidos entre deltas; esto puede
retrasar la ultima palabra hasta el siguiente separador o cierre.

Si una herramienta se invoco y luego fallo o devolvio datos invalidos, se conserva
EffectsMayHaveOccurred/effects_may_have_occurred. No se crea recibo exitoso ni se
supone que el efecto no ocurrio. Revisar evidencia antes de repetir esa accion.
Los journals nuevos contienen datos normalizados/redactados; los historicos no se
reescriben y pueden conservar datos privados de revisiones anteriores.

## Uso con archivos acotados

```powershell
dotnet run --project src/AgentLoop.Cli -- interactive --no-shell --scope-root "C:\proyectos\demo"
dotnet run --project src/AgentLoop.Cli -- goal "Crea y lee nota.txt" --cwd "C:\proyectos\demo" --no-shell --scope-root "C:\proyectos\demo" --artifact nota.txt
dotnet run --project src/AgentLoop.Cli -- journal "Crea y lee nota.txt" --cwd "C:\proyectos\demo" --no-shell --scope-root "C:\proyectos\demo"
```

Las opciones aplican a interactive, goal/run/lobos y journal/GoalRunner.
El registro tambien aplica scope a chat interactivo/run-chat; esas utilidades
conservan su ejecutor conversacional y no acreditan tarjetas de operacion.
--scope-root tambien bloquea shell arbitrario: una whitelist de paths no puede
contener lo que ejecuta PowerShell/bash. Las tools ejecutar/lanzar se ocultan del
catalogo ofrecido y se vuelven a rechazar en admision/politica si se proponen.
where_command sigue siendo un resolver fijo con ArgumentList, no shell arbitrario.
--no-shell solo no acota archivos: usar ambos si se necesita la restriccion de raiz.

Se protegen local-secrets, .git y .agentloop/memory.json/security-policy.json en
las tools de archivo aun sin raiz explicita. Los enlaces/reparse points y alias
Windows (puntos/espacios finales, dispositivos y ADS) se rechazan. Puede existir
una carrera externa entre validar una ruta y usarla: no es un sandbox de sistema.

## Limites de seguridad restantes

El modo auto predeterminado conserva shell para construir y ejecutar proyectos.
Con shell arbitrario, un proceso tiene permisos del usuario y puede eludir una
proteccion aplicada solo a tools de archivos, leer variables o usar la red.
Estos cambios no ofrecen aislamiento de procesos/red, autenticidad criptografica
de memoria ni prevencion absoluta de prompt injection. Para documentos hostiles,
usar las restricciones anteriores y un aislamiento de sistema si se requiere
contencion fuerte. Plugins/tools ejecutables siguen siendo codigo de confianza.
Los DTOs de proyectos se agregaron el 3 de octubre (seccion siguiente). Otros dominios pueden ampliarse cuando su estructura lo
requiera; los resultados variables no acreditan automaticamente su semantica.

Enfoque basado en validacion, separacion de datos y privilegios limitados:
[OWASP Prompt Injection Prevention](https://cheatsheetseries.owasp.org/cheatsheets/LLM_Prompt_Injection_Prevention_Cheat_Sheet.html).
La guia se uso como referencia de capas, no como prueba de inmunidad.
Evidencia local: [validacion de seguridad](../evidence/2026-10-01-seguridad/README.md).

## Perfil de proyectos, 3 de octubre de 2026

Los DTOs de proyecto y ejecucion se implementaron en src/AgentLoop.Core/Projects/Contracts.
Campos desconocidos, duplicados, tipos incompatibles y constructor requerido
incompleto se rechazan. Los snapshots recuperados se validan antes de restaurar
estado. Evidencia se vincula a contrato, intento, entradas y ambiente del host.

El perfil isolated-project-v1 elimina shell arbitrario de las herramientas del
modelo. Solo hay perfiles de comandos del host y archivos declarados bajo root.
La raiz excluye el harness, oraculos, secretos y journals. El contenedor recibe
proyecto/oraculos readonly, usuario no root, red none, capabilities retiradas,
no-new-privileges, memoria/CPU/PIDs acotados y tmpfs. No recibe variables de claves.
Se fija la imagen local por ID y se confirma limpieza aun tras cancelacion.
[Opciones Docker](https://docs.docker.com/engine/containers/run/).

Los defectos se etiquetan como datos sin confianza; project_evidence solo acepta
IDs registrados. Recibos, hashes y decisiones pertenecen al host. Esto limita
poisoning de contexto y falsa evidencia; no prueba seguridad exhaustiva de codigo
malicioso ni cambia la politica de shell de los goals generales.
[Ocho verificaciones reales y limites](../evidence/2026-10-03-proyectos/README.md).

## Core/Kernel y perfiles externos

El perfil lo instala el host antes de consultar al modelo. La admision estructural
se separa de Bind de capacidades. Los handlers validan su definicion al admitir;
los IDs y comandos de dominio se resuelven mediante registros inyectables.
Raiz de proyecto independiente del repo, perfil y evidencia. Los recursos Docker
se fijan por resourceHashes y se comprueban antes de comandos; imagen fijada por ID.
El modelo recibe solo comandos modelCallable. Las notas de contexto son datos sin
confianza vinculados a hash y no acreditan criterios. El perfil queda registrado
en snapshot y resultados; su cambio bloquea reutilizacion de evidencia.
[Guia vigente](../architecture/core-kernel.md). Los DTOs de dominio viven ahora en src/AgentLoop.Core/Projects/Contracts.
