using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
public static class FileTools
{

    public static DelegateTool WriteTextFile { get; } = new(
        "write_text_file", "Escribe texto UTF-8 en un archivo. Acepta rutas absolutas y rutas relativas al directorio actual.",
        """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""",
        async (arguments, token) =>
        {
            var path = ToolArguments.ResolvePath(arguments, "path");
            var content = ToolArguments.ReadRequiredString(arguments, "content");
            if (content.Length > 1000000) throw new InvalidOperationException("tope de escritura: un millon de caracteres");
            token.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllTextAsync(temporary, content, new System.Text.UTF8Encoding(false), token);
                token.ThrowIfCancellationRequested();
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return JsonSerializer.Serialize(new { path, bytes = new FileInfo(path).Length, written = true });
        });

    public static DelegateTool ReadTextFile { get; } = new(
        "read_text_file", "Lee un archivo UTF-8 y devuelve su contenido y número de líneas.",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
        async (arguments, token) =>
        {
            var path = ToolArguments.ResolvePath(arguments, "path");
            await using var stream = File.OpenRead(path);
            if (stream.Length > 1000000) throw new InvalidOperationException("archivo demasiado grande; usa una lectura acotada por shell");
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            var content = await reader.ReadToEndAsync(token);
            return JsonSerializer.Serialize(new { path, content, lines = content.Split('\n').Length });
        });

    public static DelegateTool ListDirectory { get; } = new(
        "list_directory", "Lista hasta 200 archivos y directorios con sus rutas absolutas.",
        """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""",
        (arguments, _) =>
        {
            var path = ToolArguments.ResolvePath(arguments, "path");
            var entries = Directory.EnumerateFileSystemEntries(path).Take(201).ToArray();
            return ValueTask.FromResult(JsonSerializer.Serialize(new { path, entries = entries.Take(200), truncated = entries.Length > 200 }));
        });

    public static DelegateTool FindFiles { get; } = new(
        "find_files", "Busca archivos por nombre (*.txt, memo*) bajo una raíz real. Devuelve rutas absolutas y límites alcanzados; omite carpetas inaccesibles y enlaces.",
        """{"type":"object","properties":{"root":{"type":"string"},"pattern":{"type":"string"},"max_results":{"type":"integer"},"max_depth":{"type":"integer"}},"required":["root","pattern"]}""",
        (arguments, cancellationToken) =>
        {
            var root = ToolArguments.ResolvePath(arguments, "root");
            var pattern = ToolArguments.ReadRequiredString(arguments, "pattern");
            var max = Math.Clamp(ToolArguments.ReadInt(arguments, "max_results", 100), 1, 1000);
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
            if (pattern.IndexOfAny(['/', '\\']) >= 0)
                throw new ArgumentException("pattern es un nombre con comodines; indica el directorio en root.");
            var maxDepth = Math.Clamp(ToolArguments.ReadInt(arguments, "max_depth", 12), 0, 64);
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
}
