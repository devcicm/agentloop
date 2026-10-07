# Proposito y alcance de AgentLoop

Actualizado: 7 de octubre de 2026. Describe la implementacion actual; el
[plan de publicacion](plans/publicacion-github-2026-10-07.md) describe lo pendiente.

## Que construye este repositorio

AgentLoop es un harness de agentes con un host de consola. Convierte un
objetivo del operador en una operacion local: el modelo propone acciones,
el harness admite herramientas y el host ejecuta efectos. El cierre exige
evidencia observada y comprobada mediante tarjetas transaccionales.

El proposito es ofrecer un motor reutilizable para coordinar agentes,
herramientas, procesos, contexto y recuperacion. Los contratos y perfiles
externos describen cada proyecto o tarea; las reglas del negocio pertenecen
a esos recursos externos. El POS mencionado en informes anteriores fue un
ejercicio y no es parte del producto distribuido.

## A quien sirve

- Operadores que necesitan tareas locales con comandos, archivos y evidencia.
- Desarrolladores que integran herramientas, validadores o proveedores mediante puertos.
- Equipos que quieren saber que se ejecuto, que falta y por que una operacion no cerro.

Puede participar en la construccion de repos de programacion mediante sus
herramientas y perfiles instalados. El resultado se acepta segun los criterios
configurados: crear archivos no demuestra por si solo que una aplicacion funciona.

## Responsabilidades

| Componente | Proposito |
| --- | --- |
| Kernel | Vocabulario de eventos, contratos neutrales, puertos y senales asincronas |
| Core | Tarjetas, criterios, estado de proyectos, conocimiento y presupuesto de contexto |
| Harness | Coordinacion por eventos, admision, permisos, herramientas y recuperacion |
| Infrastructure | Adaptadores de proveedor, disco, credenciales, procesos y entornos |
| CLI | Entrada del operador, controles, presentacion y composicion del host |
| Compatibility | Ejecutores anteriores disponibles mediante entradas explicitas |
| SelfTests | Verificador y fixtures; separado de las bibliotecas de produccion |

La implementacion remota actual usa NVIDIA NIM. El puerto `ILLMClient` separa
al proveedor del motor; tener el puerto no significa que otros proveedores
ya esten implementados o validados. Los modelos del catalogo tampoco garantizan
disponibilidad, acceso o capacidad de herramientas.

## Principios del sistema

1. **Eventos como eje.** Objetivos, resultados, salida de procesos y controles
   habilitan handlers. Las llamadas al modelo son trabajo dentro de la operacion.
2. **Autoridad del host.** El modelo propone; el codigo valida alcance, permisos,
   argumentos, tarjetas y evidencia. Texto de un archivo no concede permisos.
3. **Efectos observables.** Planear o responder no equivale a crear un artefacto.
4. **Cierre explicito.** Una tarjeta de cierre aceptada termina una operacion cumplida.
5. **Contexto acotado.** Historial, memoria y resultados tienen presupuestos y procedencia.
6. **Recuperacion prudente.** Un efecto incierto requiere reconciliacion antes de repetirlo.

Las esperas usan senales asincronas, incluida `AsyncManualResetEvent` para la
puerta de pausa y `TaskCompletionSource` para aprobaciones. Una pausa bloquea
nuevas admisiones; no implica suspender procesos que ya corren. La presencia de
ciclos de llamada al modelo o de una consola que lee comandos no cambia el eje
del coordinador. `run-chat` conserva un ejecutor conversacional anterior y debe
distinguirse de las entradas actuales por eventos.

## Capacidades y limites

- `interactive`: texto libre, `run` y `chat` usan operaciones por eventos.
- Terminal: `ejecutar` para comandos cortos y `lanzar` para procesos largos,
  con salida/fin observables; respeta politica y disponibilidad del interprete.
- Documentos: lectura, code view, creacion y edicion con recibos de version/rango.
- Proyectos: admision de contrato/perfil, dependencias, validaciones y recuperacion.
- Evidencia: journal y registros de operacion; informes historicos conservan su revision.

El shell usa los permisos del usuario y no es un sandbox. La deteccion de
comandos destructivos/opacos es heuristica. Los permisos continuos viven en la
sesion y no eliminan aprobaciones puntuales de riesgo detectado. El protocolo
de lectura previa se exige en herramientas tipadas; shell puede escribir por
otras vias. Ver [seguridad](guides/seguridad-datos-harness.md) y
[permisos/editor](guides/comandos-permisos-edicion.md).

La calidad semantica depende de validadores instalados. Las capturas antiguas
y una compilacion correcta no acreditan automaticamente la ultima version.
La publicacion estable y el soporte multiplataforma siguen pendientes del plan.

## Como leer el repo

1. [README y arranque](../README.md).
2. [Flujos operativos](architecture/flujos-operativos.md).
3. [Arquitectura Core/Kernel](architecture/core-kernel.md).
4. [Proyectos y perfiles](architecture/goals-proyectos.md).
5. [Contexto y memoria](guides/contexto-y-memoria.md).
6. [Plan de primera publicacion](plans/publicacion-github-2026-10-07.md).

Los nombres de eventos, tools y entradas de consola de estas guias corresponden
al codigo actual. `docs/archive/`, `docs/incidents/` y `docs/evidence/` contienen
procedencia e informes fechados; consultar su commit antes de usar sus resultados
como evidencia de una release nueva.
