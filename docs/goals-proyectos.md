# Goals de proyectos: contratos y admision P0

Actualizado: 2 de octubre de 2026. Implementado: **P0** del
[plan por fases](plan-goals-proyectos-pos-2026-10-02.md).
P1-P6 siguen pendientes. Un contrato admitido no significa proyecto construido,
POS funcional, backend aislado disponible ni goal cumplido.

## Uso sin proveedor

Desde la raiz del repo:

```powershell
dotnet run -- project-admit --project-contract "examples/project-goals/pos-minimo.example.json"
```

Para resolver rutas desde otro directorio:

```powershell
dotnet run -- project-admit --project-contract "C:\proyectos\mi contrato.json" --cwd "C:\proyectos"
```

No requiere API key, no consulta NVIDIA y no ejecuta herramientas, procesos,
instaladores ni comandos de proyecto. Lee el contrato y comprueba las rutas
existentes; no crea la raiz del proyecto. Imprime JSON con `status=admitted`,
`executionAvailable=false`, `contractHash` y el contrato normalizado.
La salida puede guardarse mediante redireccion de consola como registro de admision.
Un error de contrato/archivo devuelve codigo 64; admision correcta devuelve 0.

`goal --project-contract ...`, `run`, `journal` y las demas entradas con esta
opcion validan el archivo y rechazan su ejecucion antes de construir el cliente
LLM. No caen silenciosamente en el goal textual. En el CLI interactivo, usar
el comando explicito `project-admit`; arrancar interactive con el flag de
contrato tambien queda bloqueado. Goals sin esta opcion conservan su ruta vigente.

## Flujo implementado

```text
project-admit --project-contract <archivo>
                    |
        resolver contra --cwd (o cwd actual)
                    |
        proteger ruta y rechazar enlaces
                    |
        leer UTF-8 con limite de bytes
                    |
        StrictJson + DTO estricto
                    |
        validar presupuestos/rutas/referencias
                    |
        validar productores y grafo aciclico
                    |
        normalizar root + serializar revision
                    |
        snapshot inmutable + hash SHA-256
                    |
        JSON de admision, executionAvailable=false

intento de ejecucion con contrato
                    |
        misma admision -> bloqueo P0
                    |
        sin cliente remoto, tools o procesos
```

El runner acepta `RunnerOptions.ProjectContractPath`; `Run.ProjectContract`
permite transportar un snapshot ya admitido. Si ambos difieren se rechazan.
`GoalRunner` y `GoalRunFactory` bloquean la ejecucion antes del setup. Estos
son puntos de integracion para P1, no un nuevo ejecutor de proyectos.
La admision no publica eventos de operacion: aun no hay una operacion activa.

## Contratos y reglas

Los DTOs viven en `Goals/Projects/Contracts/ProjectGoalDto.cs`; la admision y
el snapshot en `Goals/Projects/ProjectContractAdmission.cs`.
Campos camelCase, sensibles a mayusculas. Se rechazan campos desconocidos,
propiedades duplicadas, tipos incompatibles, campos requeridos ausentes y null
en estructuras obligatorias. Las propiedades opcionales de parameters son
`commandProfile` y `scenario`; las demas deben declararse explicitamente.

| Dato | Regla actual |
| --- | --- |
| version / revision | Version 1; revision entre 1 y 1000000 |
| goalId e ids | ASCII alfanumerico, punto, guion o guion bajo; 1..128 caracteres |
| objective | 1..32000 caracteres; sin controles ni patrones de secretos detectados |
| root | Absoluta o relativa a cwd; directorio existente o futuro; no se crea |
| Colecciones principales | Entre 1 y 128 elementos cada una; ids unicos por coleccion |
| dependsOn | Referencias existentes, sin duplicados, autorreferencias ni ciclos |
| Artefactos | Un productor por artefacto; rutas unicas dentro de root |
| expectedKind | file o json |
| changePolicy | must-change o allow-existing |
| Criterios | Al menos un artefacto obligatorio; cada tarea exige una validacion obligatoria |
| Cobertura | Todo artefacto obligatorio aparece en una validacion obligatoria |
| Entradas de validacion | Artefactos de la tarea o de sus dependencias transitivas |
| executionProfile | isolated-project-v1; declaracion de perfil futuro, no aislamiento activo |

Rutas: rechazar controles, alias Windows/ADS, dispositivos Windows, recursos
protegidos y reparse points en la ruta o sus ancestros. Una ruta relativa de
artefacto no puede escapar de root. No se eliminan ni sobrescriben archivos.
La comprobacion es puntual; cambios concurrentes del sistema de archivos
requieren nueva admision y controles al ejecutar las fases posteriores.

Limites: archivo/JSON de 262144 bytes UTF-8, profundidad 32. UTF-8 estricto;
se permite BOM UTF-8, no UTF-16. El JSON admitido se serializa en orden de
propiedades del DTO, con root absoluta. El orden de arrays forma parte del hash.
El hash identifica el contenido; no constituye una firma ni autentica al operador.
Acceder a `AdmittedProjectContract.Contract` devuelve una copia nueva para que
modificar una lista obtenida no altere el snapshot autoritativo.

### Presupuestos admitidos

| Campo | Rango |
| --- | --- |
| deadlineSeconds | 1..86400 |
| maxModelCalls | 1..1000 |
| maxInputTokens | 1024..1000000 |
| maxOutputTokens | 64..32768 |
| maxCorrectionsPerWorkItem | 0..20 |
| maxConcurrentProcesses | 1..16 |
| timeoutSeconds de validador | 1..deadlineSeconds |

Son valores declarativos; la compatibilidad con contexto/modelo, la medicion
y su aplicacion se implementaran en P3. P0 no consume este presupuesto.

### Perfiles reconocidos para el contrato

| validatorId | Parametros aceptados |
| --- | --- |
| artifact | artifactIds; sin commandProfile ni scenario |
| build | artifactIds, commandProfile=node-build-v1; sin scenario |
| process-probe | artifactIds, commandProfile=node-service-v1, scenario=health-v1 |
| pos-functional | artifactIds, commandProfile=node-service-v1, scenario=pos-minimo-v1 |

Todos exigen artifactIds no vacio. Son identificadores previstos por el plan;
**los validadores ejecutables todavia no estan implementados**. No admiten
texto arbitrario de shell. Su reconocimiento no demuestra disponibilidad de
Node, navegador, contenedor o dependencias.

## Ejemplo POS y pendientes

[pos-minimo.example.json](../examples/project-goals/pos-minimo.example.json)
describe estructura -> catalogo -> ventas -> entrega; declara fuentes,
interfaz, dependencias bloqueadas y README. Los escenarios funcionales previstos
son los del plan (venta, cambio, stock, persistencia, atomicidad e idempotencia).
El fixture no es el POS ni contiene su implementacion. Los 1800 segundos y
demas presupuestos del ejemplo son valores iniciales sin calibracion remota.

Siguiente fase: P1, estado de entregables, habilitacion por eventos, asociacion
de tarjetas a tareas y distincion entre cierre de tarea y cierre del goal.
P2 implementara validadores y evidencia; P4 permitira comandos reales aislados.
Mantener el bloqueo de ejecucion hasta que la aceptacion y el entorno requeridos
puedan acreditarse. No desactivar el bloqueo simplemente para obtener un cierre.

## Comprobacion de esta entrega

Se compilo el proyecto; el registro final se conserva en
[evidencia P0](evidence/2026-10-02-proyectos-p0/README.md).
No se agregaron ni ejecutaron pruebas funcionales, no hubo inferencia remota
y no se genero el POS. Los checks historicos no verifican esta nueva admision.
