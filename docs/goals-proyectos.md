# Proyectos genericos por eventos

Actualizado: 3 de octubre de 2026. [Arquitectura Core/Kernel](core-kernel.md).
El ejercicio POS fue retirado del producto; los proyectos y perfiles son entradas
externas del host. No hay perfil implicito ni dependencia de un lenguaje concreto.

## Ejemplo generico

Desde la raiz del repo (.NET 9):

```powershell
dotnet build AgentLoop.sln
dotnet run -- project-admit --project-contract "examples/project-goals/minimal.example.json" --project-profile "examples/profiles/artifacts.example.json"
dotnet run -- project-run --project-contract "examples/project-goals/minimal.example.json" --project-profile "examples/profiles/artifacts.example.json" --fixture --fixture-root "examples/fixtures/minimal"
```

El contrato de ejemplo usa una raiz hermana al harness. `--fixture` selecciona un
cliente determinista y requiere archivos explicitamente indicados; no usa NVIDIA.
El perfil de ejemplo acredita presencia y estructura JSON. No prueba funcionalidad
de una aplicacion. `must-change` exige cambios respecto a baseline; repetir sobre
archivos identicos requiere una raiz nueva o una politica apropiada del contrato.

Para usar un modelo, omitir `--fixture` y elegir `--model <id>`. El CLI compone
el proveedor configurado; un host embebido inyecta su propio ILLMClient. La clave
permanece en su almacen dedicado. `goal`, `run` y `journal` con contrato usan la
misma ruta de proyectos. El CLI interactivo acepta `project-run ...`.

## Archivos del contrato y del perfil

El contrato version 1 declara goalId, revision, objective, root, workItems,
artifacts, validations, budgets y executionProfile. JSON estricto: campos requeridos,
sin duplicados ni campos desconocidos, grafo aciclico, rutas acotadas y referencias
coherentes. `project-admit` sin perfil hace admision estructural; con perfil tambien
comprueba capacidades. No ejecuta herramientas ni crea root.

El perfil define:

| Seccion | Responsabilidad |
| --- | --- |
| id / version | Identidad y revision de formato |
| instructions | Requisitos externos del host para este tipo de trabajo |
| validators | IDs y handler instalado; commandId, commandProfile y scenario opcionales |
| commands | ID, argumentos separados, timeout y permiso modelCallable |
| execution | Adaptador y configuracion; settings para extensiones |
| policy | Presupuestos de exploracion/progreso, bytes, notas, evidencia y journal |

Core/Projects contiene los DTOs y el estado. Kernel/Projects contiene los puertos
y DTOs de perfil. ProjectServices vincula capacidades y factories; el hash del
perfil se guarda en snapshot y resultados. Los perfiles se mantienen fuera de
root. No se permite generar dentro del repo del harness.

Para Docker, [docker.template.json](../examples/profiles/docker.template.json) es
una plantilla que **requiere reemplazar los placeholders**. Preparar imagen,
ejecutable y bundle externos. resourceHashes declara SHA-256 de recursos del
validador; el host los comprueba antes de los comandos. No se descarga una imagen
automaticamente ni se incluye un Dockerfile de aplicacion en el core.

```powershell
dotnet run -- project-check-backend --project-contract "contrato.json" --project-profile "perfil.json"
dotnet run -- project-check-backend --project-contract "contrato.json" --project-profile "perfil.json" --command "<check-del-perfil>"
```

Sin --command solo acredita preflight; un check de seguridad o funcionalidad debe
estar instalado en el perfil. Este comando requiere root existente.

## Ejecucion y contexto

```text
contrato + perfil -> bind/preflight -> MISMO actor / EventBus / supervisor
                                            |
                            tarea + intento + tarjeta abrir
                                            |
                     archivos declarados / comandos registrados
                                            |
                           project_validate [worker asincrono]
                                            |
                        resultado host -> comprobar -> cerrar hijo
                                            |
                               habilitar dependencia
                                            |
                          validar integracion -> cerrar raiz
```

Cerrar hijos no completa el goal. Toda validacion obligatoria debe estar vigente
por intento, perfil e inputs. Los recibos reales siguen siendo obligatorios.
Pausa y reanudacion usan la puerta manual compartida; cancelacion drena workers
y exige limpieza del adaptador. No se agrega otro bus ni un loop de turnos.

El brief conserva hashes, lecturas y notas etiquetadas. `project_note` exige hash
actual; no acredita criterio funcional. `project_evidence` acepta evidence_id y
offset; devuelve next_offset para consultar el diagnostico sin saturar el prompt.
Lecturas nuevas tienen presupuesto de exploracion; relecturas sin cambio consumen
el limite de falta de progreso. Las notas quedan desactualizadas si cambia el hash.

## Recuperacion

```powershell
dotnet run -- project-inspect --project-contract "contrato.json" --resume-journal "<journal>"
dotnet run -- project-reconcile --project-contract "contrato.json" --resume-journal "<journal>" --action-id "<id>" --decision applied --note "Estado observado"
dotnet run -- project-run --project-contract "contrato.json" --project-profile "perfil.json" --resume-journal "<journal>"
```

El journal es autoridad y el checkpoint una proyeccion atomica. Un efecto incierto
requiere inspeccion y decision explicita; no se repite automaticamente. Recuperar
conserva deadline, llamadas, correcciones, baseline y conocimiento. Un perfil
cambiado o snapshot antiguo sin profileHash no permite reutilizar evidencia.

## Evidencia y limites

[Entrega Core/Kernel](evidence/2026-10-03-core-kernel/README.md): compilacion y cambios.
[Evidencia historica del ejercicio](evidence/2026-10-03-proyectos/README.md) corresponde
a 5132301; sus pruebas no acreditan la version abstracta actual. Los checks
existentes se adaptaron a una fixture generica; no se ejecutaron nuevamente en
esta entrega. El comportamiento remoto posterior a estos cambios sigue sin medir.
