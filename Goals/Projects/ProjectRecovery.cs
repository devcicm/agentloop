using System.Security.Cryptography;
using System.Text.Json;
using AgentLoop.Goals.Projects.Contracts;
using AgentLoop.Security;

namespace AgentLoop.Goals.Projects;

public sealed record ProjectRecoveryInspection(string ContractHash, ProjectSnapshotDto Snapshot, IReadOnlyList<string> UncertainActions, bool Terminal);
public sealed record ReconciliationDto(int Version, string SourceHash, string ContractHash, string ActionId, string Decision, string Note,
    IReadOnlyDictionary<string, string> ObservedHashes, DateTimeOffset At);

public static class ProjectRecovery
{
    public static ProjectRecoveryInspection Inspect(string path, AdmittedProjectContract contract) => InspectCore(path, contract, 0);

    private static ProjectRecoveryInspection InspectCore(string path, AdmittedProjectContract contract, int depth)
    {
        if (depth > 32) throw new InvalidOperationException("Cadena de recuperacion demasiado larga o ciclica.");
        string? sourceJournal = null;
        string? sourceHash = null;
        ProjectContractAdmission.CheckResource(Path.GetFullPath(path));
        if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new IOException("Journal excede presupuesto de recuperacion (256 MiB).");
        var pending = new HashSet<string>(StringComparer.Ordinal);
        ProjectSnapshotDto? snapshot = null;
        var matched = false;
        var terminal = false;
        var lines = File.ReadLines(path).GetEnumerator();
        using (lines)
        {
            while (lines.MoveNext())
            {
                JsonElement entry;
                if (lines.Current.Length > 4 * 1024 * 1024) throw new InvalidOperationException("Entrada de journal demasiado grande.");
                try { entry = StrictJson.Parse(lines.Current, 4 * 1024 * 1024); }
                catch (JsonException)
                {
                    if (lines.MoveNext()) throw new InvalidOperationException("Journal corrupto antes de su ultima linea.");
                    break; // Only the last torn write can be ignored; preceding admitted actions remain uncertain.
                }
                var phase = entry.GetProperty("Phase").GetString();
                if (phase is not ("received" or "applied" or "admitted" or "resolved" or "terminal")) throw new InvalidOperationException("Fase desconocida en journal.");
                var item = entry.GetProperty("Event");
                var dataText = item.GetProperty("Data");
                if (dataText.ValueKind == JsonValueKind.String)
                {
                    var data = StrictJson.Parse(dataText.GetString()!, 4 * 1024 * 1024);
                    if (data.TryGetProperty("contractHash", out var hash))
                    {
                        if (hash.GetString() != contract.ContractHash) throw new InvalidOperationException("Journal pertenece a otra revision de contrato.");
                        matched = true;
                    }
                    if (data.TryGetProperty("sourceJournal", out var source) && source.ValueKind == JsonValueKind.String)
                    {
                        sourceJournal = source.GetString();
                        sourceHash = data.GetProperty("sourceHash").GetString();
                    }
                    if (data.TryGetProperty("action_id", out var action))
                    {
                        if (!Guid.TryParseExact(action.GetString(), "N", out _)) throw new InvalidOperationException("Identidad de accion invalida en journal.");
                        // Model calls are pure requests; unfinished effectful tools require explicit reconciliation.
                        if (phase == "admitted" && item.GetProperty("Source").GetString() == "coordinator"
                            && data.TryGetProperty("tool", out var tool) && tool.GetString() is "write_text_file" or "project_command" or "project_validate")
                            pending.Add(action.GetString()!);
                        if (phase == "resolved") pending.Remove(action.GetString()!);
                    }
                }
                if (entry.TryGetProperty("State", out var state) && state.ValueKind == JsonValueKind.Object)
                {
                    if (state.TryGetProperty("projectSnapshot", out var saved) && saved.ValueKind == JsonValueKind.Object)
                        snapshot = JsonSerializer.Deserialize<ProjectSnapshotDto>(saved.GetRawText(), ProjectJson.Options);
                    if (phase == "resolved" && state.TryGetProperty("EffectsMayHaveOccurred", out var uncertain) && uncertain.ValueKind == JsonValueKind.True)
                    {
                        var data = StrictJson.Parse(dataText.GetString()!);
                        pending.Add(data.GetProperty("action_id").GetString()!);
                    }
                }
                terminal |= phase == "terminal";
            }
        }
        if (sourceJournal is not null)
        {
            if (JournalHash(sourceJournal) != sourceHash) throw new InvalidOperationException("El journal de origen cambio despues de recuperar.");
            var parent = InspectCore(sourceJournal, contract, depth + 1);
            if (parent.UncertainActions.Count > 0) throw new InvalidOperationException("Reconciliar primero el journal de origen: " + sourceJournal);
        }
        if (!matched || snapshot is null || snapshot.Version != 1 || snapshot.ContractHash != contract.ContractHash)
            throw new InvalidOperationException("Journal sin contrato/snapshot de proyecto compatible.");
        if (snapshot.Work is null || snapshot.Results is null || snapshot.Baseline is null
            || snapshot.Work.Any(w => w is null) || snapshot.Results.Any(r => r is null || r.InputHashes is null))
            throw new InvalidOperationException("Snapshot con estructuras null.");
        if (snapshot.ModelCalls < 0 || snapshot.ModelCalls > contract.Contract.Budgets.MaxModelCalls
            || snapshot.Baseline.Count != contract.Contract.Artifacts.Count
            || contract.Contract.Artifacts.Any(a => !snapshot.Baseline.ContainsKey(a.Id))) throw new InvalidOperationException("Snapshot inconsistente.");
        var stateCheck = new ProjectState(contract.Contract);
        stateCheck.Restore(snapshot.Work);
        var knownValidations = contract.Contract.Validations.ToDictionary(v => v.Id);
        if (snapshot.Results.Select(r => r.ValidationId).Distinct().Count() != snapshot.Results.Count)
            throw new InvalidOperationException("Validaciones duplicadas en snapshot.");
        foreach (var result in snapshot.Results)
            if (!knownValidations.TryGetValue(result.ValidationId ?? "", out var specification)
                || result.ContractHash != contract.ContractHash || !Guid.TryParseExact(result.EvidenceId, "N", out _)
                || result.ValidatorVersion != "1" || result.InputHashes.Count != specification.Parameters.ArtifactIds.Count
                || specification.Parameters.ArtifactIds.Any(id => !result.InputHashes.ContainsKey(id))
                || result.Passed && result.InputHashes.Values.Contains("missing"))
                throw new InvalidOperationException("Resultado de validacion inconsistente en snapshot.");
        var currentHash = Hash(path);
        foreach (var action in pending.ToArray())
        {
            var sidecar = path + ".reconciliation." + action + ".json";
            if (!File.Exists(sidecar)) continue;
            var json = ReadBounded(sidecar);
            var decision = JsonSerializer.Deserialize<ReconciliationDto>(json, ProjectJson.Options)!;
            if (decision.Version != 1 || decision.SourceHash != currentHash || decision.ContractHash != contract.ContractHash
                || decision.ActionId != action || decision.Decision is not ("applied" or "not-applied"))
                throw new InvalidOperationException("Reconciliacion incompatible.");
            pending.Remove(action);
        }
        return new(contract.ContractHash, snapshot, pending.ToArray(), terminal);
    }

    public static string JournalHash(string path)
    {
        ProjectContractAdmission.CheckResource(Path.GetFullPath(path));
        if (new FileInfo(path).Length > 256L * 1024 * 1024) throw new IOException("Journal excede presupuesto.");
        return Hash(path);
    }

    public static IReadOnlyList<Guid> RunIds(string path, AdmittedProjectContract contract)
    {
        Inspect(path, contract); // Validate complete chain before using identities.
        var ids = new List<Guid>();
        for (var depth = 0; depth <= 32 && path is not null; depth++)
        {
            string? parent = null;
            foreach (var line in File.ReadLines(path))
            {
                JsonElement entry;
                try { entry = StrictJson.Parse(line, 4 * 1024 * 1024); }
                catch (JsonException) { break; }
                var item = entry.GetProperty("Event");
                var id = item.GetProperty("RunId").GetGuid();
                if (!ids.Contains(id)) ids.Add(id);
                if (item.GetProperty("Data").ValueKind == JsonValueKind.String)
                {
                    var data = StrictJson.Parse(item.GetProperty("Data").GetString()!, 4 * 1024 * 1024);
                    if (data.TryGetProperty("sourceJournal", out var source) && source.ValueKind == JsonValueKind.String) parent = source.GetString();
                }
            }
            path = parent!;
        }
        return ids;
    }

    public static ProjectSnapshotDto Load(string path, AdmittedProjectContract contract)
    {
        var inspected = Inspect(path, contract);
        if (inspected.UncertainActions.Count > 0) throw new InvalidOperationException("Reconciliacion requerida para acciones: " + string.Join(", ", inspected.UncertainActions));
        return inspected.Snapshot; // Acceptance is revalidated; no effect is replayed here.
    }

    public static void Reconcile(string path, AdmittedProjectContract contract, string actionId, string decision, string note)
    {
        var inspected = Inspect(path, contract);
        if (!inspected.UncertainActions.Contains(actionId) || actionId.Length != 32 || actionId.Any(c => !char.IsAsciiHexDigit(c)))
            throw new InvalidOperationException("actionId no corresponde a una accion incierta.");
        if (decision is not ("applied" or "not-applied")) throw new InvalidOperationException("decision debe ser applied o not-applied; unknown mantiene el bloqueo.");
        var cleanNote = DataGuard.Input(note);
        var hashes = new Dictionary<string, string>();
        foreach (var artifact in contract.Contract.Artifacts)
        {
            var file = Path.GetFullPath(artifact.RelativePath, contract.Root);
            ProjectContractAdmission.CheckResource(file);
            hashes[artifact.Id] = File.Exists(file) ? Hash(file) : "missing";
        }
        var record = new ReconciliationDto(1, Hash(path), contract.ContractHash, actionId, decision, cleanNote, hashes, DateTimeOffset.UtcNow);
        using var fileStream = new FileStream(path + ".reconciliation." + actionId + ".json", FileMode.CreateNew, FileAccess.Write);
        JsonSerializer.Serialize(fileStream, record, ProjectJson.Options);
        fileStream.Flush(true);
    }

    public static void SaveCheckpoint(string journalPath, ProjectSnapshotDto snapshot)
    {
        var path = journalPath + ".checkpoint.json";
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
        {
            JsonSerializer.Serialize(file, new { version = 1, journalLength = new FileInfo(journalPath).Length, snapshot }, ProjectJson.Options);
            file.Flush(true);
        }
        File.Move(temporary, path, overwrite: true);
    }
    private static string ReadBounded(string path)
    {
        ProjectContractAdmission.CheckResource(Path.GetFullPath(path));
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new IOException("Archivo excede presupuesto.");
        return StrictJson.Parse(File.ReadAllText(path), 4 * 1024 * 1024).GetRawText();
    }
    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
}
