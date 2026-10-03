using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentLoop.Goals.Projects.Contracts;
using AgentLoop.Security;

namespace AgentLoop.Goals.Projects;

/// <summary>Immutable admitted snapshot. DTO access returns a fresh copy, never authoritative mutable state.</summary>
public sealed class AdmittedProjectContract
{
    internal AdmittedProjectContract(string json, string root)
    {
        CanonicalJson = json;
        Root = root;
        ContractHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
    public string CanonicalJson { get; }
    public string ContractHash { get; }
    public string Root { get; }
    public ProjectGoalDto Contract => JsonSerializer.Deserialize<ProjectGoalDto>(CanonicalJson, ProjectJson.Options)!;
}

/// <summary>Pure admission: reads a bounded contract, validates references and paths, creates no project or process.</summary>
public static class ProjectContractAdmission
{
    public const int MaxContractBytes = 262144;
    private const int MaxEntries = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static AdmittedProjectContract Load(string contractPath, string workingDirectory)
    {
        var cwd = Path.GetFullPath(workingDirectory);
        var file = Path.GetFullPath(contractPath, cwd);
        ValidateResource(file);
        using var input = File.OpenRead(file);
        using var bounded = new BoundedReadStream(input, MaxContractBytes);
        using var reader = new StreamReader(bounded, StrictUtf8, detectEncodingFromByteOrderMarks: false);
        var text = reader.ReadToEnd();
        // Permit only a UTF-8 BOM; reject UTF-16 and invalid UTF-8 rather than replacing bytes.
        return Admit(text.TrimStart('\uFEFF'), cwd);
    }

    public static AdmittedProjectContract Admit(string json, string workingDirectory)
    {
        if (StrictUtf8.GetByteCount(json) > MaxContractBytes) throw Invalid("Contrato excede 262144 bytes UTF-8.");
        var element = StrictJson.Parse(json, MaxContractBytes);
        var dto = JsonSerializer.Deserialize<ProjectGoalDto>(element.GetRawText(), ProjectJson.Options)
            ?? throw Invalid("El contrato debe ser un objeto JSON.");
        if (dto.Version != 1) throw Invalid("Version de contrato no soportada; se requiere 1.");
        Id(dto.GoalId);
        Range(dto.Revision, 1, 1000000, "revision");
        Text(dto.Objective, 32000, "objective");
        if (DataGuard.Input(dto.Objective) != dto.Objective) throw Invalid("objective contiene controles o secretos; corregirlo antes de admitir.");
        PathText(dto.Root);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dto.Root, Path.GetFullPath(workingDirectory)));
        ValidateResource(root);
        if (File.Exists(root)) throw Invalid("root debe ser un directorio, no un archivo.");
        if (dto.ExecutionProfile != "isolated-project-v1") throw Invalid("executionProfile no soportado: usar isolated-project-v1.");
        if (dto.Budgets is null) throw Invalid("Falta budgets.");
        Range(dto.Budgets.DeadlineSeconds, 1, 86400, "deadlineSeconds");
        Range(dto.Budgets.MaxModelCalls, 1, 1000, "maxModelCalls");
        Range(dto.Budgets.MaxInputTokens, 1024, 1000000, "maxInputTokens");
        Range(dto.Budgets.MaxOutputTokens, 64, 32768, "maxOutputTokens");
        Range(dto.Budgets.MaxCorrectionsPerWorkItem, 0, 20, "maxCorrectionsPerWorkItem");
        Range(dto.Budgets.MaxConcurrentProcesses, 1, 16, "maxConcurrentProcesses");

        var work = Index(dto.WorkItems, x => x.Id, "workItems");
        var artifacts = Index(dto.Artifacts, x => x.Id, "artifacts");
        var validations = Index(dto.Validations, x => x.Id, "validations");
        if (!artifacts.Values.Any(a => a.Required)) throw Invalid("Se requiere al menos un artefacto obligatorio.");
        var paths = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var artifact in artifacts.Values)
        {
            PathText(artifact.RelativePath);
            if (Path.IsPathRooted(artifact.RelativePath)) throw Invalid("relativePath debe ser relativa.");
            var path = Path.GetFullPath(artifact.RelativePath, root);
            if (!Inside(path, root) || path == root) throw Invalid("Artefacto fuera de root: " + artifact.Id);
            ValidateResource(path);
            if (!paths.Add(path)) throw Invalid("Dos artefactos comparten ruta.");
            if (artifact.ExpectedKind is not ("file" or "json")) throw Invalid("expectedKind debe ser file o json.");
            if (artifact.ChangePolicy is not ("must-change" or "allow-existing")) throw Invalid("changePolicy desconocida.");
        }
        foreach (var item in work.Values)
        {
            References(item.DependsOn, work, "dependsOn");
            References(item.ArtifactIds, artifacts, "artifactIds");
            References(item.ValidationIds, validations, "validationIds");
            if (item.DependsOn.Contains(item.Id, StringComparer.Ordinal)) throw Invalid("Una tarea depende de si misma.");
            if (!item.ValidationIds.Any(id => validations[id].Required)) throw Invalid("Cada tarea exige al menos una validacion obligatoria.");
            foreach (var id in item.ValidationIds)
                if (validations[id].WorkItemId != item.Id) throw Invalid("Validacion asociada a una tarea diferente.");
        }
        var producers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in work.Values)
            foreach (var artifactId in item.ArtifactIds)
                if (!producers.TryAdd(artifactId, item.Id)) throw Invalid("Artefacto con mas de una tarea productora: " + artifactId);
        foreach (var artifactId in artifacts.Keys)
            if (!producers.ContainsKey(artifactId)) throw Invalid("Artefacto sin tarea productora: " + artifactId);
        foreach (var validation in validations.Values)
        {
            if (!work.TryGetValue(validation.WorkItemId ?? "", out var owner) || !owner.ValidationIds.Contains(validation.Id, StringComparer.Ordinal))
                throw Invalid("Validacion sin tarea propietaria.");
            if (validation.Parameters is null) throw Invalid("Falta parameters.");
            References(validation.Parameters.ArtifactIds, artifacts, "parameters.artifactIds");
            if (validation.Parameters.ArtifactIds.Count == 0) throw Invalid("Una validacion debe declarar sus artefactos de entrada.");
            Range(validation.TimeoutSeconds, 1, dto.Budgets.DeadlineSeconds, "timeoutSeconds");
            // These IDs describe the planned host capabilities, not installed executable validators.
            var p = validation.Parameters;
            var correct = validation.ValidatorId switch
            {
                "artifact" => p.CommandProfile is null && p.Scenario is null,
                "build" => p.CommandProfile == "node-build-v1" && p.Scenario is null,
                "process-probe" => p.CommandProfile == "node-service-v1" && p.Scenario == "health-v1",
                "pos-functional" => p.CommandProfile == "node-service-v1" && p.Scenario == "pos-minimo-v1",
                _ => false,
            };
            if (!correct) throw Invalid("ValidatorId o parametros no reconocidos: " + validation.Id);
        }
        foreach (var artifact in artifacts.Values.Where(a => a.Required))
            if (!work.Values.Any(w => w.ArtifactIds.Contains(artifact.Id, StringComparer.Ordinal))
                || !validations.Values.Any(v => v.Required && v.Parameters.ArtifactIds.Contains(artifact.Id, StringComparer.Ordinal)))
                throw Invalid("Artefacto obligatorio sin tarea y validacion obligatoria: " + artifact.Id);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw Invalid("Ciclo de dependencias en workItems.");
            foreach (var dependency in work[id].DependsOn) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
        }
        foreach (var id in work.Keys) Visit(id);
        // Compute each ancestor set once: a dense DAG must not cause exponential admission work.
        var inputCache = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        HashSet<string> AvailableInputs(string id)
        {
            if (inputCache.TryGetValue(id, out var cached)) return cached;
            var inputs = new HashSet<string>(work[id].ArtifactIds, StringComparer.Ordinal);
            foreach (var dependency in work[id].DependsOn) inputs.UnionWith(AvailableInputs(dependency));
            return inputCache[id] = inputs;
        }
        foreach (var validation in validations.Values)
            if (validation.Parameters.ArtifactIds.Any(id => !AvailableInputs(validation.WorkItemId).Contains(id)))
                throw Invalid("Validacion consume artefactos sin dependencia declarada: " + validation.Id);
        // Normalize root for a stable admitted revision; array order remains part of the contract hash.
        return new(JsonSerializer.Serialize(dto with { Root = root }, ProjectJson.Options), root);
    }

    public static void CheckExecutionRoot(string root)
    {
        CheckResource(root);
        var hostRoot = Configuration.SecretStore.ResolveRepositoryRoot();
        var relative = Path.GetRelativePath(root, hostRoot);
        if (relative == "." || !Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("root de proyecto no puede contener el repo del harness.");
        var insideHost = Path.GetRelativePath(hostRoot, root);
        if (!Path.IsPathRooted(insideHost) && insideHost != ".." && !insideHost.StartsWith(".." + Path.DirectorySeparatorChar)
            && !insideHost.StartsWith("artifacts" + Path.DirectorySeparatorChar + "generated" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dentro del harness, root debe estar bajo artifacts/generated/; los oraculos y el codigo del host son protegidos.");
    }

    public static void CheckResource(string path) => ValidateResource(Path.GetFullPath(path));

    private static Dictionary<string, T> Index<T>(IReadOnlyList<T>? values, Func<T, string> key, string field) where T : class
    {
        if (values is null || values.Count is < 1 or > MaxEntries) throw Invalid(field + " exige entre 1 y 128 elementos.");
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value is null) throw Invalid(field + " contiene null.");
            var id = key(value);
            Id(id);
            if (!result.TryAdd(id, value)) throw Invalid("Id duplicado en " + field + ": " + id);
        }
        return result;
    }

    private static void References<T>(IReadOnlyList<string>? ids, Dictionary<string, T> target, string field)
    {
        if (ids is null || ids.Count > MaxEntries) throw Invalid(field + " invalido.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
        {
            Id(id);
            if (!seen.Add(id) || !target.ContainsKey(id)) throw Invalid("Referencia duplicada o inexistente en " + field + ": " + id);
        }
    }

    private static void Id(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 128 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')))
            throw Invalid("Id invalido: usar ASCII alfanumerico, punto, guion o guion bajo; maximo 128.");
    }

    private static void Text(string? value, int maximum, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw Invalid(field + " vacio o demasiado grande.");
    }

    private static void PathText(string? path)
    {
        Text(path, 4096, "ruta");
        if (path!.Any(char.IsControl) || DataGuard.Display(path) != path || ProtectedResources.HasUnsafeWindowsAlias(path))
            throw Invalid("Ruta contiene controles, secretos o alias no permitidos.");
        if (OperatingSystem.IsWindows())
            foreach (var segment in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = segment.Split('.')[0];
                if (name.Equals("CON", StringComparison.OrdinalIgnoreCase) || name.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("AUX", StringComparison.OrdinalIgnoreCase) || name.Equals("NUL", StringComparison.OrdinalIgnoreCase)
                    || name.Length == 4 && (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && name[3] is >= '1' and <= '9')
                    throw Invalid("Nombre de dispositivo Windows no permitido.");
            }
    }

    private static bool Inside(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return path.Equals(root, comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);
    }

    private static void ValidateResource(string path)
    {
        PathText(path);
        if (ProtectedResources.IsProtected(path)) throw Invalid("Ruta protegida no permitida.");
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            // GetAttributes also detects reparse points whose targets do not exist.
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) throw Invalid("Ruta con enlace/reparse point no permitida.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    private static void Range(int value, int minimum, int maximum, string field)
    {
        if (value < minimum || value > maximum) throw Invalid(field + " fuera de rango: " + minimum + ".." + maximum);
    }
    private static InvalidOperationException Invalid(string message) => new("Contrato de proyecto: " + message);
}
