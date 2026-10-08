using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentLoop.Graphs;

namespace AgentLoop.Tools;

public sealed record RepositoryReviewOptions(int MaxFiles = 256, int MaxFileBytes = 256000, int MaxTotalBytes = 2000000,
    int MaxSymbolsPerFile = 64, int ExcerptCharacters = 400,
    IReadOnlyList<string>? Extensions = null, IReadOnlyList<string>? ExcludedDirectories = null);

public sealed record CodeReference(string Path, int Line, string Hash, string Observation, string Basis);
public sealed record CodeReviewReport(string Root, string Purpose, IReadOnlyList<CodeReference> References,
    IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges, IReadOnlyList<IReadOnlyList<string>> Cycles,
    int InspectedFiles, int SkippedFiles, bool Partial, IReadOnlyList<string> Unknowns);

/// <summary>Mapa con hashes y referencias acotadas. No ejecuta codigo; interpretaciones del LLM no son evidencia del host.</summary>
public sealed class RepositoryReviewTools(RepositoryReviewOptions? options = null)
{
    private readonly RepositoryReviewOptions _options = options ?? new();
    public ToolRegistry Registry() => new([
        new DelegateTool("project_map", "Mapea archivos y relaciones observadas del repo; conserva hashes, cobertura y referencias. focus acota el contexto; no ejecuta ni escribe.", Schema,
            (args, token) => ReviewAsync(args, token), ToolCapabilities.Observation),
        new DelegateTool("code_review", "Inspecciona contexto del repo para explicar proposito y flujos o revisar defectos. Devuelve referencias verificables y limites; lee codigo adicional con code_view antes de afirmar detalles o editar.", Schema,
            (args, token) => ReviewAsync(args, token), ToolCapabilities.Observation)
    ]);
    private const string Schema = """{"type":"object","properties":{"root":{"type":"string"},"focus":{"type":"string"},"max_files":{"type":"integer"}},"required":["root"]}""";

    private async ValueTask<string> ReviewAsync(JsonElement args, CancellationToken token)
    {
        var root = ToolArguments.ResolvePath(args, "root");
        ProjectContractAdmission.CheckResource(root);
        if (!Directory.Exists(root)) throw new ArgumentException("root debe ser directorio.");
        var maximum = Math.Clamp(ToolArguments.ReadInt(args, "max_files", _options.MaxFiles), 1, _options.MaxFiles);
        var focus = ToolArguments.ReadString(args, "focus") ?? "";
        var extensions = (_options.Extensions ?? [".cs", ".csproj", ".sln", ".json", ".md", ".py", ".js", ".mjs", ".ts", ".go", ".rs", ".java"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded = (_options.ExcludedDirectories ?? [".git", ".vs", ".agentloop", "local-secrets", "bin", "obj", "node_modules", "vendor"]).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var graph = new DirectedGraph(maximum * (_options.MaxSymbolsPerFile + 1));
        var references = new List<CodeReference>(); var paths = new List<string>(); var pending = new Stack<string>(); pending.Push(root);
        var skipped = 0; var partial = false; var directories = 0;
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            if (++directories > maximum * 8) { partial = true; break; }
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
            {
                token.ThrowIfCancellationRequested();
                var info = File.GetAttributes(entry);
                if (info.HasFlag(FileAttributes.ReparsePoint) || ProtectedResources.IsProtected(entry)) { skipped++; continue; }
                if (info.HasFlag(FileAttributes.Directory)) { if (!excluded.Contains(Path.GetFileName(entry))) pending.Push(entry); else skipped++; continue; }
                var name = Path.GetFileName(entry);
                if (name.StartsWith(".env", StringComparison.OrdinalIgnoreCase) || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("credential", StringComparison.OrdinalIgnoreCase) || name.Contains("api-key", StringComparison.OrdinalIgnoreCase)) { skipped++; continue; }
                if (!extensions.Contains(Path.GetExtension(entry)) || focus.Length > 0 && !Path.GetRelativePath(root, entry).Contains(focus, StringComparison.OrdinalIgnoreCase)) continue;
                if (paths.Count >= maximum) { partial = true; break; }
                paths.Add(entry);
            }
            if (partial) break;
        }
        long total = 0; var inspected = 0; var manifests = new Dictionary<string,string>();
        foreach (var path in paths)
        {
            token.ThrowIfCancellationRequested(); ProjectContractAdmission.CheckResource(path);
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > _options.MaxFileBytes || total + file.Length > _options.MaxTotalBytes) { skipped++; partial = true; continue; }
            var bytes = new byte[(int)file.Length]; await file.ReadExactlyAsync(bytes, token); total += bytes.Length;
            TextDocumentSnapshot document;
            try { document = TextDocumentSnapshot.Decode(path, bytes); } catch (Exception error) when (error is DecoderFallbackException or InvalidOperationException) { skipped++; partial = true; continue; }
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            var id = "file:" + relative;
            graph.AddNode(new(id, "file", relative, document.Hash)); inspected++;
            var text = DataGuard.Redact(document.Text);
            if (Path.GetExtension(path).Equals(".csproj",StringComparison.OrdinalIgnoreCase)) manifests[path] = text;
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var excerpt = string.Join("\n", lines.Take(8));
            references.Add(new(relative, 1, document.Hash, TextSanitizer.Truncate(excerpt, _options.ExcerptCharacters), "observed-text"));
            foreach (Match match in Regex.Matches(text, @"(?m)^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|export|async)\s+)*(?:class|interface|record|struct|enum|def|function|fn)\s+([\p{L}_][\p{L}\p{N}_]*)", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Take(_options.MaxSymbolsPerFile))
            {
                var line = text.AsSpan(0, match.Index).Count('\n') + 1;
                var symbol = id + "#" + match.Groups[1].Value + ":" + line;
                graph.AddNode(new(symbol, "symbol-candidate", relative, document.Hash, match.Groups[1].Value));
                graph.AddEdge(new(id, symbol, "contains-text-declaration", Verified: false));
            }
        }
        // Relacion de proyecto observada en manifiestos; no inventa relaciones de llamadas.
        foreach (var path in manifests.Keys)
        {
            var id = "file:" + Path.GetRelativePath(root, path).Replace('\\', '/');
            if (!graph.Nodes.Any(n => n.Id == id)) continue;
            ProjectContractAdmission.CheckResource(path);
            foreach (Match match in Regex.Matches(manifests[path], "<ProjectReference\\s+Include=\"([^\"]+)\"", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            {
                var targetPath = Path.GetFullPath(match.Groups[1].Value, Path.GetDirectoryName(path)!);
                var target = "file:" + Path.GetRelativePath(root, targetPath).Replace('\\', '/');
                if (graph.Nodes.Any(n => n.Id == target)) graph.AddEdge(new(id, target, "project-reference-candidate", Verified: false));
            }
        }
        var report = new CodeReviewReport(root, "Contexto observado para explicacion/revision; el modelo debe justificar su resumen con estas referencias.", references,
            graph.Nodes.ToArray(), graph.Edges, graph.StrongComponents().Where(c => c.Count > 1).ToArray(), inspected, skipped, partial,
            ["No se ejecuto codigo ni validadores.", "Simbolos son candidatos por texto; relaciones de llamadas requieren parser semantico.", "La cobertura puede ser parcial; usa focus y code_view para ampliar."]);
        var serialized = JsonSerializer.Serialize(report);
        while (serialized.Length > DataLimits.PromptOutputCharacters - 2048)
        {
            if (report.References.Count == 0 && report.Nodes.Count == 0 && report.Edges.Count == 0) throw new InvalidOperationException("Metadatos de revision fuera de presupuesto.");
            report = report with { References = report.References.Take(report.References.Count/2).ToArray(), Nodes = report.Nodes.Take(report.Nodes.Count/2).ToArray(),
                Edges = report.Edges.Take(report.Edges.Count/2).ToArray(), Cycles = [], Partial = true };
            serialized = JsonSerializer.Serialize(report);
        }
        return serialized;
    }
}
