# Changelog

## 0.1.0-alpha.1 â€” candidato local, sin publicacion

- Bibliotecas Kernel/Core, Harness, Infrastructure, Compatibility y CLI separadas.
- Operaciones por eventos, tarjetas y cierre con evidencia del host.
- NVIDIA NIM mediante puerto LLM; catalogo no garantiza disponibilidad de modelos.
- Chat interactivo unificado con run; run-chat conserva el ejecutor anterior.
- Scripts multilinea y here-strings; permisos puntuales y continuos revocables.
- Code view y edicion de texto/palabras/lineas/parrafos con lectura previa.
- Documentacion de proposito, arquitectura, flujos y preparacion de releases.
- Version/SDK fijados; empaquetado Windows x64 y workflows de validacion local.
- Corregida regla artifacts/ que ocultaba cuatro implementaciones de Infrastructure al clonar.
- Runtime autocontenido fijado en 9.0.20; auditoria compara version real del ZIP y manifiesto.

### Limites de la preview

- .NET 9: migracion a .NET 10 pendiente antes de una entrega estable.
- Shell no es un sandbox; riesgo detectado por heuristicas configurables.
- Edicion tipada conserva version/rango; shell puede escribir por otras vias.
- Licencia pendiente hasta eleccion del titular; no distribuir como open source.
- Selftest requiere fuentes y SDK; no viene incluido en el ZIP del CLI.
- Linux/macOS, NuGet, firma de binarios y actualizaciones automaticas pendientes.
- Evidencia del candidato: consultar docs/evidence y el manifiesto del paquete.

### Preparacion local posterior al candidato

- Wizard `setup` con entrada oculta, consulta de estado y escritura local restringida.
- Comprobacion de exclusion Git y rechazo de enlaces antes de guardar credenciales.
- Retirado el fallback de claves entre proveedores; salida sin fragmentos del secreto.
- Documentados limites y pendientes agenticos. El ZIP anterior no incluye estos cambios.

### Resultados y credenciales (8 de octubre)

- Result y Failure en fronteras de comandos, credenciales y cierre del reactor.
- Errores del proveedor codificados sin persistir cuerpos ni mensajes externos.
- Actividades de trazabilidad y registro local de metadatos con rotacion.
- Arranque detecta ausencia/formato y consulta aceptacion del catalogo; opcion offline.

## Implementaci?n pendiente de publicaci?n ? 2026-10-08

- Mailbox por eventos acotado, supervisi?n de tareas y parada ante efectos inciertos.
- Pol?ticas confiables, presupuestos compartidos de inferencia/reintentos y progreso verificable.
- Intenciones, sesiones v2, contratos de prompt y skills del operador.
- Mapa de repositorio, revisi?n acotada y algoritmos de grafos generales.
- Preview firmado de edici?n, respaldos restringidos y restauraci?n con hash y permiso puntual.
- Restricci?n de or?genes HTTPS y redirecciones del proveedor.
