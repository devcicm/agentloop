# Changelog

## 0.1.0-alpha.1 — candidato local, sin publicacion

- Bibliotecas Kernel/Core, Harness, Infrastructure, Compatibility y CLI separadas.
- Operaciones por eventos, tarjetas y cierre con evidencia del host.
- NVIDIA NIM mediante puerto LLM; catalogo no garantiza disponibilidad de modelos.
- Chat interactivo unificado con run; run-chat conserva el ejecutor anterior.
- Scripts multilinea y here-strings; permisos puntuales y continuos revocables.
- Code view y edicion de texto/palabras/lineas/parrafos con lectura previa.
- Documentacion de proposito, arquitectura, flujos y preparacion de releases.
- Version/SDK fijados; empaquetado Windows x64 y workflows de validacion local.

### Limites de la preview

- .NET 9: migracion a .NET 10 pendiente antes de una entrega estable.
- Shell no es un sandbox; riesgo detectado por heuristicas configurables.
- Edicion tipada conserva version/rango; shell puede escribir por otras vias.
- Licencia pendiente hasta eleccion del titular; no distribuir como open source.
- Selftest requiere fuentes y SDK; no viene incluido en el ZIP del CLI.
- Linux/macOS, NuGet, firma de binarios y actualizaciones automaticas pendientes.
- Evidencia del candidato: consultar docs/evidence y el manifiesto del paquete.
