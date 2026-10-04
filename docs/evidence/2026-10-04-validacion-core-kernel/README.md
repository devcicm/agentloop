# Validacion Core/Kernel y proyectos externos

Fecha: 4 de octubre de 2026 (America/Mexico_City).
Base revisada: 6085025. El cambio previo en .github/copilot-instructions.md pertenece al usuario y se conserva.

## Antes y correcciones

- La primera suite fallo porque la entrada de una prueba era generica y su asercion aun esperaba pos_system. Se comprueba ahora la herencia de requisitos y la exclusion del razonamiento.
- Una ruta Docker no ejecutable produjo Win32Exception sin controlar. El adaptador traduce ese fallo a IOException y el CLI responde 64, sin iniciar el modelo.
- project-admit ignoraba --project-profile aunque la guia indicaba vinculacion de capacidades. Ahora vincula cuando se proporciona perfil y expone capabilitiesBound y profileHash.
- La fixture reinicia la fase de integracion cuando se habilita un intento de reparacion; un recibo anterior no sustituye una nueva validacion.

## Resultados

- Compilacion: [build.txt](build.txt).
- Suite completa: 430 comprobaciones correctas, 48 de proyectos. [selftest.txt](selftest.txt).
- Pruebas de proyectos: [projects.txt](projects.txt). Incluyen memoria recuperada, notas ligadas a hashes, exploracion acotada, evidencia paginada, rechazo de perfil alterado y reparacion tras fallo de integracion usando el actor existente.
- NVIDIA GLM 5.3: [remote.txt](remote.txt), run 74abcb6c9fc24fa9a5ad5ba03eaf00d6, Succeeded. Dos archivos generados, tres validaciones obligatorias y cierre raiz; 13 llamadas al modelo en el snapshot.
- Proyecto de codigo NVIDIA: [code-remote.txt](code-remote.txt), run 2d0ecb98cfdf4c90aeefbcf9ec118320, Succeeded. Modulo ESM add(a,b) creado fuera del repo, ejecutado en Docker con cuatro casos numericos y documento comprobado. [code-recovery.txt](code-recovery.txt) verifica el journal terminal.
- Recuperacion: [recovery.txt](recovery.txt). Journal terminal, sin efectos inciertos. [resume.txt](resume.txt): run 04e77aa08e4243c694f19f6e9c997e34, Succeeded; revalida integracion sin reescribir entregables.
- Fixture Docker correcta: [fixture.txt](fixture.txt), Succeeded. Fixture incoherente: [fixture-failure.txt](fixture-failure.txt), Failed por criterio del validador; no se declara exito.
- Backend invalido: [invalid-backend.txt](invalid-backend.txt), salida 64 controlada.
- Perfil incompatible en admision: [admission-rejection.txt](admission-rejection.txt), salida 64 antes de inferencia.

Los contratos y perfiles de esta carpeta son capturas de esta PC, con rutas absolutas. Para repetir, cambiar las raices generadas a directorios nuevos externos al harness, actualizar executable/assetRoot y recalcular resourceHashes; usar una imagen local preparada. No son valores por defecto del Core ni del Kernel.

[runs.json](runs.json) conserva resultados, validaciones completas y hashes/contenido de entregables. Las trazas derivadas conservan fases, IDs y causalidad; los journals autoritativos completos permanecen en .agentloop/event-journal/.

## Flujo comprobado

```text
contrato externo + perfil host
             |
    admision y capacidades -> preflight Docker
             |
       actor / bus existente
             |
     tarea habilitada por evento
             |
       LLM propone herramientas
             |
    tarjeta -> escritura -> validacion host
             |
       resultado tipado por evento
          /              \
       valido           fallo
         |                |
 aceptar tarea    presupuesto + invalidar DAG
         |                |
 dependencia       habilitar reparacion
          \              /
            integracion
                 |
      comprobacion vigente + cierre raiz
                 |
       journal / snapshot / evidencia
```

No agrega un motor de turnos, otro bus ni dependencias de negocio al Core/Kernel.
El caso pequeno acredita los criterios instalados; no prueba sistemas de dominio completos.

GitHub: la CLI conserva devcicm pero rechaza su token. No hay remoto Git configurado. La renovacion requiere participacion del usuario en el navegador; no se exponen credenciales ni se publica el repositorio.
