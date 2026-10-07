# Contribuir a AgentLoop

La licencia de distribucion esta pendiente de eleccion. Antes de abrir el repo
al publico se deben resolver licencia, destino, titularidad y politica de aportaciones.

## Desarrollo local

Instalar el SDK exacto de global.json. Ejecutar desde una ruta con permiso de
escritura; no versionar claves, sesiones, logs ni proyectos de negocio generados.

```powershell
dotnet restore AgentLoop.sln
dotnet build AgentLoop.sln -c Release --no-restore
dotnet run --project tests/AgentLoop.SelfTests -c Release --no-build -- selftest
python scripts/audit-architecture.py
python scripts/audit-git-secrets.py
```

El verificador es un ejecutable; dotnet test no reemplaza su suite.
Pruebas remotas requieren credencial dedicada y autorizacion de consumo.
CI local no necesita NVIDIA_API_KEY ni el token personal de GitHub.

## Cambios

- Trabajar en rama y proponer PR con objetivo, alcance, evidencia y limitaciones.
- Mantener Kernel/Core independientes de disco, procesos y proveedores.
- Los perfiles externos definen dominio; no incrustar ejercicios de negocio.
- Coordinar trabajo mediante eventos, senales y admision del host.
- Mantener contratos/schema, compatibilidad de journals y sanitizacion.
- Leer los archivos antes de editar; actualizar documentos e inventario cuando cambien rutas.
- Explicar incertidumbre de efectos y recuperacion; no dar una operacion por cerrada por texto.
- No mover tags publicados ni reemplazar silenciosamente assets de una version.

Para vulnerabilidades, consultar SECURITY.md y evitar publicar secretos o exploits
contra instalaciones ajenas en issues. Este repo no tiene aun canal GitHub configurado.
