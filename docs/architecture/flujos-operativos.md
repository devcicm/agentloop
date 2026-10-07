# Flujos operativos de AgentLoop

Actualizado: 7 de octubre de 2026. Flujos actuales basados en el coordinador,
la composicion del host y las entradas del CLI. La publicacion al final es propuesta.

## 1. Capas y dependencias

Las flechas representan referencias de proyecto, no mensajes en ejecucion.

```text
AgentLoop.Kernel           --> sin referencias a otros proyectos
AgentLoop.Core             --> Kernel
AgentLoop.Harness          --> Core, Kernel
AgentLoop.Infrastructure   --> Harness, Core, Kernel
AgentLoop.Compatibility    --> Infrastructure, Harness, Core, Kernel
AgentLoop.Cli              --> todas las bibliotecas de produccion
AgentLoop.SelfTests        --> CLI y bibliotecas de produccion

Puertos internos <--- adaptadores del host
Kernel/Core       : contratos y estado
Harness           : coordinacion y decisiones
Infrastructure    : efectos y proveedor
CLI               : composicion y operador
```

Produccion no referencia el verificador. La compatibilidad permanece en su
proyecto; Core/Kernel no cargan NVIDIA, Docker ni un sistema de negocio.

## 2. Objetivo y ejecucion por eventos

```text
Operador: texto libre / run / chat dentro de interactive
                         |
                         v
                 objetivo + contexto acotado
                         |
                         v
        GoalRunFactory: bus + supervisor + tarjetas + tools + scope
                         |
                         v
                     Objetivo
                         |
                         v
          EventOperationCoordinator / mailbox serial
                         |
                         v
                RazonamientoRequerido
                         |
                         v
            ModeloSolicitado --> ILLMClient --> NVIDIA
                         |                        |
                         +<---- ModeloResultado --+
                         |
                         v
             propuesta de tarjeta / herramienta
                         |
                         v
       argumentos + alcance + reglas de tarjeta + politica
                         |
             +-----------+-----------+
             |                       |
             v                       v
     rechazo observado        accion admitida
             |                       |
             |                       v
             |          herramienta / disco / proceso
             |                       |
             |                       v
             |        resultado + eventos de salida/fin
             |                       |
             +-----------------------+
                         |
                         v
               contexto + journal + evidencia
                         |
             +-----------+---------------------+
             |                                 |
             v                                 v
     hechos habilitan trabajo         comprobar + cerrar aceptado
     pendiente / nuevas acciones                   |
             |                                     v
             +--> coordinador                objetivo cumplido
```

Este retorno al coordinador representa reaccion a hechos, no una espera basada
en turnos del usuario. El actor serializa handlers; llamadas remotas y procesos
se ejecutan de forma asincrona. Deadline, aborto, cancelacion o error pueden
terminar sin cumplimiento. El journal registra lo recibido/aplicado y resultados;
la ausencia de error en una llamada no sustituye la tarjeta de cierre.

Entradas diferentes:

```text
chat independiente --> llamada remota sin herramientas locales
run-chat           --> ejecutor conversacional anterior con tools basicas
project-run        --> contrato/perfil admitidos --> operacion por eventos
```

## 3. Controles y permisos

```text
comando original --> politica + clasificador del host
                              |
                 +------------+------------------+
                 |            |                  |
                 v            v                  v
              vetado     permitido/simulado   requiere permiso
                 |            |                  |
                 v            |                  v
              rechazo         |      AprobacionSolicitada(id, riesgo, scope)
                              |                  |
                              |      espera asincrona, sin polling
                              |                  ^
                              |                  |
                              |      operador --> Aprobacion(id, decision)
                              |                  |
                              +<--- permitido ---+
                              |
                              v
                   proceso / salida / fin / resultado

operador --> pausa        --> cierra puerta de nuevas acciones
operador --> reanudar     --> abre puerta y habilita trabajo pendiente
operador --> cancelar    --> inicia detencion de la operacion
operador --> estado      --> publica Estado
operador --> permisos revocar --> PermisosRevocados --> elimina continuos
```

El permiso continuo se limita a sesion, interprete y directorio. Un comando
detectado como destructivo/opaco sigue pidiendo permiso puntual. Revocar no
deshace efectos ni detiene por si solo procesos ya autorizados.
Detalles y here-strings: [protocolo de permisos](../guides/comandos-permisos-edicion.md).

## 4. Edicion de documentos

```text
read_text_file / code_view
            |
            v
contenido paginado + hash + read_receipt + rango observado
            |
            v
edit_text_file / sobrescribir con write_text_file
            |
            v
validar identidad + hash vigente + rango leido + coincidencias
            |
   +--------+------------------+
   |        |                  |
   v        v                  v
 error   preview            aplicar
 sin        |                  |
 escribir   v                  v
         propuesta     escritura + invalidar recibos
         sin efecto            |
                               v
                        lectura del resultado
                               |
                               v
                    evidencia / comprobar / cierre
```

`read_receipt` autoriza una edicion; `receipt_id` acredita una observacion para
tarjetas. Son contratos diferentes. Cambiar bytes invalida la lectura anterior.
Sobrescritura exige lectura completa; edicion parcial exige todo el rango elegido.
Un error despues de comenzar a escribir puede requerir reconciliacion.

## 5. Proyecto externo y recuperacion

```text
contrato de objetivo + perfil instalado por host
                       |
                       v
          admision / hashes / capacidades / scope
                       |
                       v
             dependencias habilitan tarea
                       |
                       v
       apertura --> acciones --> artefactos observados
                       |
                       v
             validadores del perfil
                       |
            +----------+------------+
            |                       |
            v                       v
        fallos/defectos       validacion vigente
            |                       |
            v                       v
      reparar y observar     aceptar tarea / habilitar siguiente
                                    |
                                    v
                     comprobacion integral + cierre del objetivo

journal + checkpoint --> inspeccion --> reconciliar si hay efectos inciertos
                                    --> reanudar trabajo admitido pendiente
```

Un cambio de artefactos puede invalidar validaciones/dependencias. Los perfiles
definen que se comprueba; el motor no presupone POS u otro dominio. Recuperar
no significa repetir automaticamente una accion cuyo efecto se desconoce.

## 6. Publicacion propuesta, todavia pendiente

```text
proposito + guias + diagramas + version + licencia elegida
                              |
                              v
       candidato --> auditoria + pruebas nuevas + build en limpio
                              |
                              v
                 PR / CI --> main --> tag fijo
                              |
                              v
                ZIP + hashes + manifiesto + notas
                              |
                              v
           borrador prerelease --> revision --> publicar
```

Criterios y prioridades: [plan de publicacion](../plans/publicacion-github-2026-10-07.md).
Estos diagramas documentan responsabilidades y flujo; no son evidencia de
pruebas nuevas, disponibilidad de modelos ni de una release ya publicada.

## Referencias del codigo

- [Coordinador](../../src/AgentLoop.Harness/Operations/EventOperationCoordinator.cs).
- [Composicion](../../src/AgentLoop.Infrastructure/Composition/GoalRunFactory.cs).
- [Politica](../../src/AgentLoop.Harness/Execution/Contracts/ExecutionPolicy.cs).
- [Aceptacion](../../src/AgentLoop.Harness/Operations/Acceptance/OperationAcceptance.cs).
- [Editor](../../src/AgentLoop.Infrastructure/Files/Editing/TextEditingTools.cs).
- [Runtime de proyectos](../../src/AgentLoop.Harness/Projects/ProjectRuntime.cs).
