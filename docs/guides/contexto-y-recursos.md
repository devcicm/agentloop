# Contexto y descubrimiento de recursos desde consola

Texto libre y `run` en `interactive` ejecutan el actor por eventos con tools de descubrimiento. El prompt incluye el entorno y la politica de trabajo. En operaciones, la admision normaliza `path` y `root` contra `--cwd`; los resultados incluyen rutas absolutas con espacios intactos. `get_system_context` informa el cwd del proceso, que puede ser distinto de `--cwd`. El `chat` independiente no tiene tools; el `chat` interactivo es una utilidad conversacional anterior.

| Herramienta | Uso |
|---|---|
| `get_system_context` | Directorio actual, sistema operativo, escritorio, documentos, perfil y temporal. No devuelve variables de entorno ni credenciales. |
| `where_command` | Localiza un ejecutable con `where.exe` en Windows o `which` en Unix. Devuelve rutas, código de salida y error. |
| `list_directory` | Lista hasta 200 entradas con rutas absolutas e indica truncamiento. |
| `find_files` | Busca nombres con comodines bajo una raíz, con profundidad y cantidad limitadas. |

Ejemplos dentro de `interactive` (para cumplir la operacion, abrir tarjeta, observar un resultado exitoso y comprobar su recibo antes de cerrar):

```text
run dime el directorio actual y la carpeta de documentos usando el contexto del sistema
run localiza dotnet con where_command y dime su ruta absoluta
run busca archivos memo*.txt debajo del directorio actual y devuelve sus rutas
run busca AgentLoop.csproj en el directorio actual, sin entrar en subdirectorios
```

`find_files` acepta `root`, `pattern`, `max_results` (100 por defecto, máximo 1000) y `max_depth` (12 por defecto, máximo 64; cero busca solo en la raíz). Omite directorios inaccesibles y enlaces. Interrumpe el recorrido al alcanzar 20.000 entradas o cinco segundos entre entradas, y señala `truncated`/`stop_reason`. Estos límites se comprueban durante la enumeración: una operación de disco bloqueada puede demorar más. Una búsqueda vacía solo corresponde al alcance y permisos indicados.

`where_command` pasa el nombre como un argumento del proceso, sin interpolarlo en un shell. Lee stdout y stderr de manera concurrente, cancela a los diez segundos y termina el proceso al cancelar. No es una herramienta para ejecutar comandos arbitrarios; `goal` dispone de `ejecutar` para operaciones de shell.

Validación realizada en Windows, 27 de septiembre de 2026:

- Compilación correcta: cero errores y las dos advertencias de nulabilidad existentes.
- Suite existente: `SELF-TEST OK`, salida 0.
- Prueba real previa con GLM 5.3: `where_command` resolvió `dotnet` como `C:\Program Files\dotnet\dotnet.exe` y el turno terminó con salida 0.
- Prueba real del contexto y búsqueda: GLM 5.3 invocó `get_system_context`, después `find_files` con `pattern=AgentLoop.csproj` y `max_depth=0`. Encontró `C:\Users\Carlos Ivan\Desktop\agent loop\AgentLoop.csproj` y terminó en `Completed`, salida 0, tres llamadas al modelo y dos herramientas.

El comportamiento Unix del resolvedor está implementado, pero estas verificaciones se ejecutaron en Windows.

Estado actual: [coordinador por eventos](../architecture/implementacion-coordinador-eventos-2026-09-30.md). Las cifras anteriores pertenecen a la revision del 27; la validacion registrada del 30 compilo sin advertencias.
