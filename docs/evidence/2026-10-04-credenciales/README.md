# Auditoria de credenciales e historial Git

Fecha local: 4 de octubre de 2026. Base: 5f0ebed.

## Resultado y alcance

[history-audit.json](history-audit.json): 73 commits, 653 blobs y 355 trees revisados con git cat-file --batch-all-objects. Incluye objetos no referenciados. Se resolvieron rutas mediante todas las refs y reflogs; se revisaron ademas los archivos versionables del arbol de trabajo.

No se encontraron credenciales reales con los detectores instalados ni coincidencias exactas con las dos claves del almacen local. No se imprimen valores ni se incluyen en este informe. Las coincidencias iniciales fueron dos versiones antiguas del README con un ejemplo sk- seguido exclusivamente por 24 letras x; se clasifican como mascara uniforme, no secreto. La afirmacion inicial de clave expuesta fue corregida tras esa comprobacion.

No se reescribio el historial ni se expiro el reflog, porque no aparecio un secreto real que requiriese limpieza. Los objetos con ejemplos siguen siendo ejemplos. La auditoria por patrones y comparacion exacta no prueba ausencia de secretos opacos o formatos desconocidos. No examina archivos runtime ignorados; el almacen dedicado se usa solo para comparacion en memoria.

## Carga y prevencion

- NVIDIA: el host llama SecretStore; admite variables de entorno o archivos dedicados de local-secrets. Los clientes reciben ApiKey mediante inyeccion. Core/Kernel no contienen credenciales ni resuelven archivos secretos.
- GitHub CLI: cuenta devcicm autenticada; token en keyring del sistema, protocolo Git HTTPS. No se copio el token al repo.
- local-secrets queda excluido recursivamente; solo README.md esta versionado. Se verifico tambien una subcarpeta ficticia con git check-ignore.
- .env y .env.* quedan excluidos. .env.example y .env.template son excepciones para plantillas sin valores reales.
- DataGuard redacta los cinco prefijos de token GitHub y github_pat_, tambien mediante SafeStreamingText con fragmentos.
- [audit-git-secrets.py](../../../scripts/audit-git-secrets.py) permite repetir la auditoria; devuelve 1 si hay candidatos y 0 si no los hay. El reporte predeterminado queda en .agentloop, excluido. No modifica el historial ni desactiva candidatos por baja entropia cuando coinciden con una clave local conocida.

La herramienta no instala hooks ni publica datos. Si una auditoria futura encuentra un valor real, revisar el candidato sin imprimirlo, revocar en el proveedor y limpiar todas las refs/objetos afectados. La reescritura de Git no revoca una credencial.

## Evidencia

- [Compilacion](build.txt): 0 errores y 0 advertencias.
- [Suite completa](selftest.txt): 436 comprobaciones correctas, incluidas seis nuevas de tokens GitHub.
- [Exclusiones y fuentes](exclusion-checks.json).
- [Validacion de redaccion y coordinador](security-tests.txt).
- [Comprobaciones sinteticas del scanner](scanner-checks.json): deteccion de token, clasificacion de mascara, comparacion exacta aun con mascara, UTF-16, credencial opaca conocida y asignacion literal.

Los cambios previos del usuario en .github/copilot-instructions.md se conservaron.
