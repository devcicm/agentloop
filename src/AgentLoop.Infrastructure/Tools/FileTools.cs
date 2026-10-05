using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
public static class FileTools
{

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
