# Instalacion y uso del candidato Windows x64

El paquete de preview se prepara localmente. No existe una descarga de release
publicada todavia. Licencia de distribucion pendiente; no redistribuir el candidato
como una version open source hasta resolverla.

## Desde codigo fuente

Instalar SDK 9.0.316, fijado en global.json, y compilar AgentLoop.sln.
La migracion a .NET 10 LTS esta pendiente antes de una entrega estable.

```powershell
dotnet build AgentLoop.sln -c Release
dotnet run --project src/AgentLoop.Cli -c Release --no-build -- interactive --politica confirmar
```

## Desde ZIP autocontenido

1. Comprobar SHA256SUMS.txt contra el ZIP. El checksum detecta cambios;
   no reemplaza una firma de autor ni la confianza en la fuente.
2. Extraer en una carpeta propia y escribible, por ejemplo bajo tu perfil de usuario.
3. Consultar release-manifest.json: commit, SDK, RID, version y limites del candidato.
4. Ejecutar AgentLoop.Cli.exe version y help. No requieren clave ni SDK.
5. Elegir un workspace escribible separado para los proyectos generados.

```powershell
& '.\AgentLoop.Cli.exe' version
& '.\AgentLoop.Cli.exe' help --plain-ui
& '.\AgentLoop.Cli.exe' interactive --politica confirmar --cwd 'C:\proyectos con espacios\demo'
```

El ZIP contiene el runtime y no necesita SDK. La compatibilidad de tu sistema
con ese runtime debe confirmarse; Windows x64 es el unico RID del candidato inicial.

## Configurar NVIDIA

Usar NVIDIA_API_KEY en el entorno del proceso o crear local-secrets junto al
binario y colocar nvidia-api-key.txt dentro, con una sola linea. Mantener esa
carpeta privada y excluida del control de versiones. Nunca incluirla en el ZIP
ni usar una clave personal para CI de PR. El ejemplo no contiene claves reales.

El host aun resuelve parte de sus datos a partir del directorio de secretos
o cwd: elegir ubicaciones escribibles y conocidas. Separar completamente
workspace, credenciales y datos es una mejora pendiente. .agentloop conserva
sesiones/evidencia local; su contenido puede ser privado.

## Entradas

- En interactive: chat, run y texto libre usan reactor y terminal.
- ejecutar: comandos cortos; lanzar: procesos largos.
- aprobar [id], aprobar [id] continuo, rechazar [id]: controles del operador.
- permisos / permisos revocar: consulta o revoca continuos de sesion.
- Para edicion tipada acotada: interactive --no-shell --scope-root <ruta> --cwd <ruta>.
- run-chat: modo conversacional anterior sin terminal; chat independiente no tiene tools.

La pausa impide nuevas acciones, no suspende procesos ya iniciados. Destructivos
u opacos detectados necesitan permiso puntual. El modo confirmar evita depender
solo de la deteccion heuristica; un shell sigue teniendo los permisos del usuario.

## Diagnostico y pruebas

Los comandos selftest son para desarrollo: requieren tests/, fuentes y SDK.
El ZIP informa de esa limitacion con un error claro; no incluye el verificador.
Ayuda/version funcionan sin credencial. Ante error de proveedor comprobar
configuracion, red, acceso y disponibilidad; catalogo no garantiza acceso.
Ante efecto incierto inspeccionar journal y reconciliar antes de repetirlo.

[Flujos](../architecture/flujos-operativos.md) ·
[Permisos/editor](comandos-permisos-edicion.md) ·
[Plan de distribucion](../plans/publicacion-github-2026-10-07.md).
