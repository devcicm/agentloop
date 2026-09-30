# Contexto y descubrimiento de recursos desde consola

El chat interactivo y `run` reciben el directorio actual, sistema operativo y catálogo de herramientas en el prompt de cada turno. Las rutas relativas se resuelven desde el directorio actual del proceso; los resultados de descubrimiento incluyen rutas absolutas con espacios intactos.

| Herramienta | Uso |
|---|---|
| `get_system_context` | Directorio actual, sistema operativo, escritorio, documentos, perfil y temporal. No devuelve variables de entorno ni credenciales. |
| `where_command` | Localiza un ejecutable con `where.exe` en Windows o `which` en Unix. Devuelve rutas, código de salida y error. |
| `list_directory` | Lista hasta 200 entradas con rutas absolutas e indica truncamiento. |
| `find_files` | Busca nombres con comodines bajo una raíz, con profundidad y cantidad limitadas. |

Ejemplos dentro de `interactive`:

```text
chat dime el directorio actual y la carpeta de documentos usando el contexto del sistema
chat localiza dotnet con where_command y dime su ruta absoluta
chat busca archivos memo*.txt debajo del directorio actual y devuelve sus rutas
chat busca AgentLoop.csproj en el directorio actual, sin entrar en subdirectorios
```

`find_files` acepta `root`, `pattern`, `max_results` (100 por defecto, máximo 1000) y `max_depth` (12 por defecto, máximo 64; cero busca solo en la raíz). Omite directorios inaccesibles y enlaces. Interrumpe el recorrido al alcanzar 20.000 entradas o cinco segundos entre entradas, y señala `truncated`/`stop_reason`. Estos límites se comprueban durante la enumeración: una operación de disco bloqueada puede demorar más. Una búsqueda vacía solo corresponde al alcance y permisos indicados.

`where_command` pasa el nombre como un argumento del proceso, sin interpolarlo en un shell. Lee stdout y stderr de manera concurrente, cancela a los diez segundos y termina el proceso al cancelar. No es una herramienta para ejecutar comandos arbitrarios; `goal` dispone de `ejecutar` para operaciones de shell.

Validación realizada en Windows, 27 de septiembre de 2026:

- Compilación correcta: cero errores y las dos advertencias de nulabilidad existentes.
- Suite existente: `SELF-TEST OK`, salida 0.
- Prueba real previa con GLM 5.3: `where_command` resolvió `dotnet` como `C:\Program Files\dotnet\dotnet.exe` y el turno terminó con salida 0.
- Prueba real del contexto y búsqueda: GLM 5.3 invocó `get_system_context`, después `find_files` con `pattern=AgentLoop.csproj` y `max_depth=0`. Encontró `C:\Users\Carlos Ivan\Desktop\agent loop\AgentLoop.csproj` y terminó en `Completed`, salida 0, tres llamadas al modelo y dos herramientas.

El comportamiento Unix del resolvedor está implementado, pero estas verificaciones se ejecutaron en Windows.
