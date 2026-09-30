using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;

/// <summary>Herramientas deterministas para ejercitar el loop sin depender de servicios externos.</summary>
public static class BuiltInTools
{
    public static DelegateTool AddNumbers { get; } = new(
        name: "add_numbers",
        description: "Suma dos numeros y devuelve el resultado exacto.",
        parametersJsonSchema: """
        {
          "type": "object",
          "properties": {
            "a": { "type": "number", "description": "Primer sumando" },
            "b": { "type": "number", "description": "Segundo sumando" }
          },
          "required": ["a", "b"]
        }
        """,
        handler: (arguments, _) =>
        {
            var a = ReadDouble(arguments, "a");
            var b = ReadDouble(arguments, "b");
            var sum = a + b;
            return ValueTask.FromResult(JsonSerializer.Serialize(new { a, b, sum }));
        });

    public static DelegateTool GetTime { get; } = new(
        name: "get_time",
        description: "Devuelve la hora UTC actual del sistema en formato ISO-8601.",
        parametersJsonSchema: """
        {
          "type": "object",
          "properties": {
            "label": { "type": "string", "description": "Etiqueta opcional para identificar la consulta" }
          }
        }
        """,
        handler: (arguments, _) =>
        {
            var label = ReadString(arguments, "label");
            return ValueTask.FromResult(JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                label,
            }));
        });

    /// <summary>Tool de sondeo: su unico proposito es ver si el modelo emite una tool call.</summary>
    public static DelegateTool GetWeather { get; } = new(
        name: "get_weather",
        description: "Get the current weather of a city.",
        parametersJsonSchema: """
        {
          "type": "object",
          "properties": {
            "city": { "type": "string", "description": "City name" }
          },
          "required": ["city"]
        }
        """,
        handler: (arguments, _) =>
        {
            var city = ReadString(arguments, "city") ?? "unknown";
            return ValueTask.FromResult(JsonSerializer.Serialize(new { city, temperature_c = 21, condition = "sunny" }));
        });

    public static DelegateTool WriteTextFile { get; } = new(
        "write_text_file", "Escribe texto UTF-8 en un archivo. Acepta rutas absolutas y rutas relativas al directorio actual.",
        """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""",
        (arguments, _) =>
        {
            var path = ResolvePath(arguments, "path");
            var content = ReadRequiredString(arguments, "content");
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
            return ValueTask.FromResult(JsonSerializer.Serialize(new { path, bytes = new FileInfo(path).Length, written = true }));
        });

    public static DelegateTool ReadTextFile { get; } = new(
        "read_text_file", "Lee un archivo UTF-8 y devuelve su contenido y número de líneas.",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
        (arguments, _) =>
        {
            var path = ResolvePath(arguments, "path");
            var content = File.ReadAllText(path);
            return ValueTask.FromResult(JsonSerializer.Serialize(new { path, content, lines = content.Split('\n').Length }));
        });

    public static DelegateTool ListDirectory { get; } = new(
        "list_directory", "Lista hasta 200 archivos y directorios con sus rutas absolutas.",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
        (arguments, _) =>
        {
            var path = ResolvePath(arguments, "path");
            var entries = Directory.EnumerateFileSystemEntries(path).Take(201).ToArray();
            return ValueTask.FromResult(JsonSerializer.Serialize(new { path, entries = entries.Take(200), truncated = entries.Length > 200 }));
        });

    public static DelegateTool GetSystemContext { get; } = new(
        "get_system_context", "Consulta el directorio actual, sistema operativo y carpetas conocidas reales. No expone credenciales ni variables de entorno.",
        """{"type":"object","properties":{}}""",
        (_, _) => ValueTask.FromResult(JsonSerializer.Serialize(new
        {
            working_directory = Environment.CurrentDirectory,
            operating_system = Environment.OSVersion.ToString(),
            executable_resolver = OperatingSystem.IsWindows() ? "where.exe" : "which",
            user_directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            desktop_directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            documents_directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            temporary_directory = Path.GetTempPath(),
        })));

    public static DelegateTool WhereCommand { get; } = new(
        "where_command", "Resuelve la ruta de un ejecutable usando where.exe en Windows o which en Unix.",
        """{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""",
        async (arguments, cancellationToken) =>
        {
            var command = ReadRequiredString(arguments, "command");
            if (command.StartsWith('-') || command.StartsWith('/') && OperatingSystem.IsWindows()
                || command.Contains('\r') || command.Contains('\n'))
                throw new ArgumentException("command debe ser un nombre o ruta de ejecutable, no opciones ni comandos de shell.");
            var resolver = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "where.exe") : "which";
            var info = new ProcessStartInfo { FileName = resolver, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            info.ArgumentList.Add(command);
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"no se pudo iniciar {resolver}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            string output;
            string error;
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                output = await stdout.ConfigureAwait(false);
                error = await stderr.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(p => Path.GetFullPath(p.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return JsonSerializer.Serialize(new { command, resolver, exit_code = process.ExitCode, paths, error = error.Trim() });
        });

    public static DelegateTool FindFiles { get; } = new(
        "find_files", "Busca archivos por nombre (*.txt, memo*) bajo una raíz real. Devuelve rutas absolutas y límites alcanzados; omite carpetas inaccesibles y enlaces.",
        """{"type":"object","properties":{"root":{"type":"string"},"pattern":{"type":"string"},"max_results":{"type":"integer"},"max_depth":{"type":"integer"}},"required":["root","pattern"]}""",
        (arguments, cancellationToken) =>
        {
            var root = ResolvePath(arguments, "root");
            var pattern = ReadRequiredString(arguments, "pattern");
            var max = Math.Clamp(ReadInt(arguments, "max_results", 100), 1, 1000);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            if (pattern.IndexOfAny(['/', '\\']) >= 0)
                throw new ArgumentException("pattern es un nombre con comodines; indica el directorio en root.");
            var maxDepth = Math.Clamp(ReadInt(arguments, "max_depth", 12), 0, 64);
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true, IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint, MaxRecursionDepth = maxDepth,
            };
            var paths = new List<string>();
            var clock = Stopwatch.StartNew();
            var scanned = 0;
            string? stopped = null;
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++scanned > 20000 || clock.Elapsed > TimeSpan.FromSeconds(5))
                {
                    stopped = scanned > 20000 ? "max_entries" : "time_limit";
                    break;
                }
                if (!File.Exists(entry) || !System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                    pattern, Path.GetFileName(entry), OperatingSystem.IsWindows())) continue;
                if (paths.Count == max) { stopped = "max_results"; break; }
                paths.Add(Path.GetFullPath(entry));
            }
            return ValueTask.FromResult(JsonSerializer.Serialize(new
            {
                root, pattern, count = paths.Count, paths, max_depth = maxDepth,
                scanned_entries = scanned, truncated = stopped is not null, stop_reason = stopped,
                inaccessible_directories = "skipped", symbolic_links = "skipped",
            }));
        });

    /// <summary>Set por defecto para chat, run e interactive.</summary>
    public static ToolRegistry DefaultRegistry() => new([AddNumbers, GetTime, WriteTextFile, ReadTextFile, ListDirectory, GetSystemContext, WhereCommand, FindFiles]);

    private static double ReadDouble(JsonElement arguments, string property)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(property, out var value))
        {
            throw new ArgumentException($"falta el parametro requerido '{property}'");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            // Algunos modelos mandan los numeros como string; aceptarlo evita un turno perdido.
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => throw new ArgumentException($"'{property}' no es un numero: {value.GetRawText()}"),
        };
    }

    private static string? ReadString(JsonElement arguments, string property)
        => arguments.ValueKind == JsonValueKind.Object &&
           arguments.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ReadRequiredString(JsonElement arguments, string property)
        => ReadString(arguments, property) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"falta el parametro requerido '{property}'");

    private static string ResolvePath(JsonElement arguments, string property)
    {
        var raw = ReadRequiredString(arguments, property);
        return Path.GetFullPath(raw, Environment.CurrentDirectory);
    }

    private static int ReadInt(JsonElement arguments, string property, int fallback)
        => arguments.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
}
