# Evidencia del candidato local 0.1.0-alpha.1

Fecha: 7 de octubre de 2026. Rama: release/0.1.0-alpha.1.
Commit de fuentes y paquete comprobado: `2c940130c746781c56309a2f5a6c911cf8008914`.
El commit posterior que guarda este informe no representa nuevos binarios.

## Resultado

- SDK 9.0.316 fijado; siete proyectos, build Release limpio con 0 errores y 0 advertencias.
- Clon local aislado fuera del workspace, sin claves ni archivos ignorados copiados.
- Suite local y suite del clon: 474 comprobaciones correctas; 27 del nuevo bloque de distribucion.
- Regresiones de permisos, ID, continuo, deadline, transporte here-string y editor.
- Auditoria de arquitectura sin errores; sin archivos C# ignorados fuera de bin/obj.
- Auditoria de secretos: 85 commits/1201 blobs/573 trees; cero candidatos de objetos y worktree.
- Dos workflows parseados, Actions fijadas por SHA y permisos contents:read; no ejecutados en GitHub.
- ZIP win-x64 autocontenido con runtime 9.0.20 y 217 archivos; hashes/version real y contenido sin errores.
- ZIP extraido fuera del repo en ruta con espacios: version y ayuda correctas sin clave.
- Selftest fuera del repo: diagnostico esperado de falta de checkout/verificador.
- NVIDIA GLM 5.3 desde el ZIP: Succeeded, consulta dotnet --version por ejecutar,
  tres tarjetas aceptadas, cero rechazadas, observacion de 9.0.316 y cierre comprobado.
- Bloqueo por licencia pendiente comprobado: empaquetado normal rechazado; preview interna explicita.

La credencial remota se leyo del almacen dedicado y se paso al entorno del
proceso de prueba; no se puso en argumentos, fuentes, paquete ni este informe.
Los logs completos y journals se conservan localmente en .agentloop y el
workspace temporal de la prueba; no se versionan capturas privadas.
`summary.json` identifica el ZIP, hash y alcance de estas observaciones.

## Bloqueo encontrado y solucion

La primera compilacion del clon fallo con CS0246 para ArtifactEnvironmentFactory
y ArtifactValidatorFactory. Cuatro fuentes de Execution/Artifacts existian
localmente pero Git las ignoraba por la regla artifacts/ sin anclaje.

Se anclaron ambas reglas a /artifacts/, se registraron las cuatro fuentes y
se repitieron build y suite en el clon. Esto evita que una compilacion local
con archivos invisibles sea confundida con un checkout reproducible.

Dos expectativas de pruebas anteriores se adaptaron: instalar el clasificador
en la prueba de politica y usar una lectura vigente antes de sobrescribir.
Se preservaron los controles de riesgo y lectura previa del producto.

El primer paquete elegia runtime 9.0.18 del SDK. Se fijo ReleaseRuntimeVersion
9.0.20 en la configuracion central, se comparo con deps.json desde el auditor
y se regenero el ZIP desde el clon limpio. Se repitieron arranque y prueba NVIDIA
sobre el paquete final. Los paquetes anteriores son capturas locales de preparacion,
no el candidato aprobado por este informe.

La primera prueba remota del paquete final alcanzo el deadline de 60 segundos
despues de ejecutar el comando y no cerro el objetivo. Se conservo ese registro
local y se hizo un reintento acotado a 180 segundos, que cerro correctamente.
Esto demuestra el limite efectivo y un caso exitoso; no garantiza latencia ni
disponibilidad permanente de NVIDIA.

## Pendiente antes de distribuir

Licencia/titular, destino y visibilidad GitHub, canal privado de seguridad,
activacion real de CI/rulesets y revision del borrador. No hay tag ni release.
Migracion a .NET 10 pendiente antes de estable; la preview usa .NET 9.
El paquete no tiene firma de autor y no acredita otras plataformas, otros
modelos, calidad semantica general ni todos los escenarios de concurrencia.
La auditoria por patrones no demuestra ausencia de secretos opacos desconocidos.

No se creo un repo GitHub, no se conecto origin en el workspace principal,
no se hizo push, no se publico tag ni se ejecuto workflow remoto.
El clon temporal tiene origin local; sus fetch/pull solo leen este repo local.
