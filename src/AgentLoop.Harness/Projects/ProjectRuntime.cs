using System.Collections.Concurrent;
using System.Text.Json;
namespace AgentLoop.Goals.Projects;


/// <summary>Project projection owned by the existing actor. Async validators return immutable deliveries.</summary>
public sealed class ProjectRuntime
{
    private readonly EventBus _bus;
    private readonly CardLedger _ledger;
    private readonly IProjectExecutionEnvironment _environment;
    private readonly ProjectValidationExecutor _validator;
    private readonly ConcurrentDictionary<string, IReadOnlyList<ValidationResultDto>> _deliveries = new();
    private readonly List<string> _evidenceRoots = [];
    private readonly Dictionary<string, ValidationResultDto> _results = new(StringComparer.Ordinal);
    private Dictionary<string, string> _hashes;
    private Dictionary<string, string> _baseline;
    private readonly ProjectKnowledge _knowledge = new();
    private readonly ProjectServices _services;
    private readonly ProjectProfileDto _profile;
    public ProjectRuntimePolicy Policy => _profile.Policy;
    public ProjectRecoveryPolicy RecoveryPolicy => new(Policy.MaxJournalBytes, Policy.MaxJournalEntryBytes);
    public string Instructions => _profile.Instructions;
    private readonly AsyncManualResetEvent _gate;
    public AdmittedProjectContract Admitted { get; }
    public ProjectGoalDto Contract { get; }
    public ProjectState State { get; }
    public DateTimeOffset StartedAt { get; private set; } = DateTimeOffset.UtcNow;
    public string? ResumeJournal { get; init; }
    public int ModelCalls { get; set; }
    public TimeSpan Remaining => StartedAt.AddSeconds(Contract.Budgets.DeadlineSeconds) - DateTimeOffset.UtcNow;
    public string ActiveId => State.Active?.Id ?? "__goal__";
    public string AttemptId => State.Active?.AttemptId ?? "integration";

    public ProjectRuntime(AdmittedProjectContract admitted, CardLedger ledger, EventBus bus, ProjectServices services, IProjectExecutionEnvironment? environment = null, AsyncManualResetEvent? gate = null)
    {
        Admitted = admitted;
        Contract = admitted.Contract;
        _ledger = ledger;
        _bus = bus;
        _gate = gate ?? new AsyncManualResetEvent(signaled: true);
        _services = services;
        _profile = services.Profile;
        services.Bind(admitted);
        _environment = environment ?? services.EnvironmentFactory.Create(admitted.Root, bus.RunId,
            notice => bus.Publish(notice.Started ? AgentEventKind.ProcesoIniciado : AgentEventKind.ProcesoFin,
                "project-adapter", notice.CommandId, notice));
        State = new(Contract);
        _services.Resources.CheckExecutionRoot(admitted.Root);
        _evidenceRoots.Add(Path.Combine(services.EvidenceDirectory, bus.RunId.ToString("N")));
        _validator = new(admitted, _environment, _evidenceRoots[0], _gate, services);
        _hashes = _validator.HashInputs(Contract.Artifacts.Select(a => a.Id));
        _baseline = new(_hashes, StringComparer.Ordinal);
    }

    public async Task StartAsync(CancellationToken token)
    {
        if (ResumeJournal is not null)
        {
            var snapshot = _services.Resources.LoadSnapshot(ResumeJournal, Admitted, RecoveryPolicy);
            if (snapshot.ProfileHash != _services.Hash) throw new InvalidOperationException("Perfil cambiado o journal anterior sin perfil; no se reutiliza evidencia.");
            if (snapshot.Knowledge is not null)
            {
                _knowledge.Restore(snapshot.Knowledge, Contract.Artifacts.Select(a => a.Id).ToHashSet(StringComparer.Ordinal));
                if (_knowledge.ExplorationReads > Policy.MaxExplorationReads || _knowledge.Artifacts.Any(a => (a.Note?.Length ?? 0) > Policy.MaxNoteChars))
                    throw new InvalidOperationException("Contexto recuperado excede politica.");
            }
            foreach (var previous in _services.Resources.PreviousRuns(ResumeJournal, Admitted, RecoveryPolicy))
                _evidenceRoots.Add(Path.Combine(_services.EvidenceDirectory, previous.ToString("N")));
            State.Restore(snapshot.Work);
            StartedAt = snapshot.StartedAt;
            ModelCalls = snapshot.ModelCalls;
            _baseline = new(snapshot.Baseline, StringComparer.Ordinal);
            foreach (var result in snapshot.Results) _results[result.ValidationId] = result;
            foreach (var item in State.Snapshot.Where(w => w.Status == ProjectWorkStatus.Accepted))
                if (!FreshFor(item.Id)) State.Invalidate(Contract.WorkItems.Single(w => w.Id == item.Id).ArtifactIds);
        }
        if (Remaining <= TimeSpan.Zero) throw new InvalidOperationException("Deadline original agotado; crear una nueva revision/operacion.");
        await _environment.PreflightAsync(token);
        if (ResumeJournal is not null)
            foreach (var previous in _services.Resources.PreviousRuns(ResumeJournal, Admitted, RecoveryPolicy)) await _environment.CheckPreviousRunAsync(previous, token);
        await _gate.WaitAsync(token);
        _services.Resources.CheckResource(Admitted.Root);
        _services.Resources.CreateDirectory(Admitted.Root);
        _ledger.AcceptanceObjective = ""; // Explicit project criteria supersede documentary path inference.
        var play = _ledger.Play("abrir", new Dictionary<string, string> { ["objetivo"] = Contract.Objective, ["plan"] = "Entregables del contrato " + Admitted.ContractHash });
        if (!play.Accepted) throw new InvalidOperationException(play.Rejection);
        State.EnableNext();
    }

    public void PublishStart()
    {
        _bus.Publish(AgentEventKind.ProyectoAdmitido, "project", "contrato de proyecto admitido",
            new { contractHash = Admitted.ContractHash, contract = Contract, sourceJournal = ResumeJournal, sourceHash = ResumeJournal is null ? null : _services.Resources.JournalHash(ResumeJournal, RecoveryPolicy), startedAt = StartedAt, executionEnvironment = _environment.Identity, profileHash = _services.Hash });
        _bus.Publish(AgentEventKind.TrabajoHabilitado, "project", ActiveId, new ProjectEventDataDto(1, Admitted.ContractHash, ActiveId, AttemptId));
    }

    private void EnableNext()
    {
        var next = State.EnableNext();
        _knowledge.BeginWork();
        _bus.Publish(AgentEventKind.TrabajoHabilitado, "project", next?.Id ?? "integracion final",
            new ProjectEventDataDto(1, Admitted.ContractHash, ActiveId, AttemptId));
    }

    public string Brief() => JsonSerializer.Serialize(new
    {
        contractHash = Admitted.ContractHash, workItemId = ActiveId, attemptId = AttemptId,
        work = State.Snapshot, task = Contract.WorkItems.SingleOrDefault(w => w.Id == ActiveId),
        knowledge = _knowledge.Artifacts.Select(a => new { a.ArtifactId, a.Reads, fresh = _hashes.GetValueOrDefault(a.ArtifactId) == a.Hash,
            note = a.Note is null ? null : DataGuard.PromptData("model-note:" + a.ArtifactId, a.Note, Policy.MaxNoteChars) }),
        progress = new { _knowledge.ActionsWithoutProgress, _knowledge.ExplorationReads },
        guidance = "Consulta defectos por evidence_id y conserva hallazgos con project_note. Las lecturas repetidas sin cambio no son progreso; corrige y valida antes del cierre.",
        artifactHashes = _hashes, commands = _profile.Commands.Where(c => c.ModelCallable).Select(c => c.Id),
        artifacts = Contract.Artifacts, validations = Contract.Validations.Where(v => v.WorkItemId == ActiveId || ActiveId == "__goal__"),
        defects = _results.Values.Where(v => !v.Passed).Select(v => new { v.ValidationId, v.EvidenceId, observations = DataGuard.PromptData("validator:" + v.ValidationId, v.Observations, Policy.MaxDefectChars) }),
    }, ProjectJson.Options);

    public string? Admit(ParsedToolCall call)
    {
        if (call.Name is "write_text_file" or "edit_text_file")
        {
            var path = Path.GetFullPath(call.Arguments.GetProperty("path").GetString()!, Admitted.Root);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!Contract.Artifacts.Any(a => Path.GetFullPath(a.RelativePath, Admitted.Root).Equals(path, comparison)))
                return "Solo se permite escribir artefactos declarados en el contrato.";
            if (_ledger.OpenCount != 2) return "Abrir la transaccion de tarea antes de escribir.";
        }
        if (call.Name is "project_validate" or "project_command")
        {
            if (call.Arguments.GetProperty("work_item_id").GetString() != ActiveId || call.Arguments.GetProperty("attempt_id").GetString() != AttemptId)
                return "Tarea o intento no vigente.";
            if (_ledger.OpenCount != (State.AllAccepted ? 1 : 2)) return "Transaccion de tarea no abierta.";
        }
        return null;
    }

    public string? CheckCard(CardKind kind, IReadOnlyDictionary<string, string> fields)
    {
        if (!fields.TryGetValue("work_item_id", out var work) || work != ActiveId
            || !fields.TryGetValue("attempt_id", out var attempt) || attempt != AttemptId) return "Tarjeta exige work_item_id y attempt_id vigentes.";
        if (kind == CardKind.Apertura && (State.AllAccepted || _ledger.OpenCount != 1)) return "Solo una transaccion de tarea activa.";
        if (kind != CardKind.Apertura && _ledger.OpenCount != (State.AllAccepted ? 1 : 2)) return "Transaccion de tarea no abierta.";
        if (kind is CardKind.Comprobacion or CardKind.Cierre)
        {
            if (!State.AllAccepted && kind == CardKind.Cierre && Contract.WorkItems.Single(w => w.Id == ActiveId).DependsOn.Any(id => State.Snapshot.Single(w => w.Id == id).Status != ProjectWorkStatus.Accepted))
                return "Dependencia invalidada: abortar esta tarea y reparar la dependencia habilitada.";
            if (_environment.Running != 0) return "Procesos aislados pendientes.";
            if (!FreshFor(ActiveId)) return "Validaciones obligatorias faltantes, fallidas o con entradas modificadas: ejecutar project_validate.";
            foreach (var artifact in Contract.Artifacts.Where(a => a.Required && (State.AllAccepted || Contract.WorkItems.Single(w => w.Id == ActiveId).ArtifactIds.Contains(a.Id))))
                if (artifact.ChangePolicy == "must-change" && _baseline[artifact.Id] == _validator.HashInputs([artifact.Id])[artifact.Id])
                    return "Artefacto obligatorio sin cambio desde inicio: " + artifact.Id;
        }
        return null;
    }

    private bool FreshFor(string workId)
    {
        var specs = Contract.Validations.Where(v => v.Required && (workId == "__goal__" || v.WorkItemId == workId));
        return specs.All(v => _results.TryGetValue(v.Id, out var result) && result.Passed
            && result.ContractHash == Admitted.ContractHash && result.ProfileHash == _services.Hash
            && (workId == "__goal__" ? result.AttemptId == "integration" : State.Active?.Id != workId || result.AttemptId == AttemptId)
            && result.InputHashes.Count == v.Parameters.ArtifactIds.Count
            && _validator.HashInputs(v.Parameters.ArtifactIds).All(p => result.InputHashes.TryGetValue(p.Key, out var hash) && hash == p.Value));
    }

    public bool Observe(ToolExecution execution, ParsedToolCall call)
    {
        if (execution.EffectsMayHaveOccurred)
        {
            _bus.Publish(AgentEventKind.ReconciliacionRequerida, "project", "efecto incierto; detener y reconciliar", new { call_id = call.Id });
            throw new InvalidOperationException("Efecto incierto: revisar journal y reconciliar antes de repetir.");
        }
        var changed = _validator.HashInputs(Contract.Artifacts.Select(a => a.Id));
        var differences = changed.Where(p => p.Value != _hashes[p.Key]).Select(p => p.Key).ToArray();
        State.Invalidate(differences);
        _hashes = changed;
        var progress = differences.Length > 0;
        var exploration = false;
        if (!execution.Failed && (call.Name is "read_text_file" or "code_view"))
        {
            var path = Path.GetFullPath(call.Arguments.GetProperty("path").GetString()!, Admitted.Root);
            var artifact = Contract.Artifacts.SingleOrDefault(a => string.Equals(Path.GetFullPath(a.RelativePath, Admitted.Root), path,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
            if (artifact is not null) exploration = _knowledge.ObserveRead(artifact.Id, _hashes[artifact.Id], Policy.MaxExplorationReads);
        }
        if (!execution.Failed && call.Name == "project_note")
        {
            var artifact = call.Arguments.GetProperty("artifact_id").GetString()!;
            _knowledge.Note(artifact, _hashes[artifact], DataGuard.Input(call.Arguments.GetProperty("note").GetString()!));
        }
        if (execution.ToolName == "project_validate")
        {
            using var data = JsonDocument.Parse(execution.Output);
            if (data.RootElement.TryGetProperty("batch_id", out var batch) && _deliveries.TryRemove(batch.GetString()!, out var results))
            {
                foreach (var result in results)
                {
                    progress |= result.Passed && (!_results.TryGetValue(result.ValidationId, out var old) || !old.Passed || !old.InputHashes.OrderBy(p => p.Key).SequenceEqual(result.InputHashes.OrderBy(p => p.Key)));
                    _results[result.ValidationId] = result;
                    _bus.Publish(result.Passed ? AgentEventKind.ValidacionFinalizada : AgentEventKind.ValidacionFallida,
                        "validator", result.ValidationId, result);
                }
                var failures = results.Where(r => !r.Passed).ToArray();
                if (failures.Length > 0)
                {
                    if (call.Arguments.GetProperty("work_item_id").GetString() == "__goal__")
                    {
                        var affected = Contract.Validations.Where(v => failures.Any(r => r.ValidationId == v.Id)).ToArray();
                        foreach (var owner in affected.Select(v => v.WorkItemId).Distinct()) State.RecordValidationFailure(owner);
                        State.Invalidate(affected.SelectMany(v => v.Parameters.ArtifactIds));
                    }
                    else State.RecordValidationFailure();
                }
            }
        }
        if (call.Name == "tarjeta" && !execution.Failed)
        {
            var card = call.Arguments.GetProperty("nombre").GetString();
            if (card == "cerrar" && !State.AllAccepted)
            {
                var acceptedId = ActiveId;
                State.AcceptCurrent(); progress = true;
                _bus.Publish(AgentEventKind.TrabajoAceptado, "project", acceptedId, new ProjectEventDataDto(1, Admitted.ContractHash, acceptedId, call.Arguments.GetProperty("attempt_id").GetString()!));
                EnableNext();
            }
            else if (card == "abortar" && !State.AllAccepted) { State.AbortCurrent(); EnableNext(); }
        }
        if (State.Active is null && !State.AllAccepted && _ledger.OpenCount == 1) EnableNext();
        _knowledge.RecordAction(progress, exploration);
        if (_knowledge.ActionsWithoutProgress > Policy.MaxActionsWithoutProgress)
            throw new InvalidOperationException("Sin progreso material; presupuesto configurado de acciones agotado.");
        return progress;
    }

    public ProjectSnapshotDto Snapshot() => new(1, Admitted.ContractHash, StartedAt, ModelCalls, State.Snapshot, _results.Values.Select(r => r with { Observations = Prompting.TextSanitizer.Truncate(r.Observations, Policy.MaxSnapshotObservationChars) }).ToArray(), _baseline, _services.Hash, _knowledge.Snapshot());

    public ToolRegistry Tools() => new([
        new DelegateTool("project_note", "Guarda un hallazgo de contexto vinculado al hash del artefacto; no acredita validacion ni progreso.",
            """{"type":"object","properties":{"artifact_id":{"type":"string"},"hash":{"type":"string"},"note":{"type":"string"}},"required":["artifact_id","hash","note"]}""",
            (args, token) =>
            {
                var id = args.GetProperty("artifact_id").GetString()!;
                if (!_hashes.TryGetValue(id, out var hash) || args.GetProperty("hash").GetString() != hash)
                    throw new InvalidOperationException("Nota exige hash vigente del artefacto.");
                var note = args.GetProperty("note").GetString()!;
                if (note.Length > Policy.MaxNoteChars) throw new InvalidOperationException("Nota excede politica.");
                return ValueTask.FromResult(JsonSerializer.Serialize(new { artifact_id = id, hash, stored = true }));
            }),
        new DelegateTool("project_evidence", "Lee observaciones de evidencia del host por id, sin admitir rutas arbitrarias.",
            """{"type":"object","properties":{"evidence_id":{"type":"string"},"offset":{"type":"integer","minimum":0}},"required":["evidence_id"]}""",
            (args, token) =>
            {
                var id = args.GetProperty("evidence_id").GetString();
                if (!Guid.TryParseExact(id, "N", out _) || !_results.Values.Any(r => r.EvidenceId == id)) throw new InvalidOperationException("Evidencia no registrada en esta operacion.");
                var path = _evidenceRoots.Select(root => Path.Combine(root, id + ".json")).FirstOrDefault(_services.Resources.Exists)
                    ?? throw new FileNotFoundException("Registro completo no disponible; repetir validacion.");
                _services.Resources.CheckResource(path);
                if (_services.Resources.Length(path) > Policy.MaxEvidenceBytes) throw new IOException("Evidencia excede presupuesto.");
                var result = JsonSerializer.Deserialize<ValidationResultDto>(StrictJson.Parse(_services.Resources.ReadText(path)).GetRawText(), ProjectJson.Options)!;
                if (result.EvidenceId != id || result.ContractHash != Admitted.ContractHash || result.ProfileHash != _services.Hash)
                    throw new InvalidOperationException("Registro de evidencia incompatible.");
                var offset = args.TryGetProperty("offset", out var offsetValue) ? offsetValue.GetInt32() : 0;
                var text = DataGuard.Display(result.Observations);
                if (offset < 0 || offset > text.Length) throw new InvalidOperationException("Offset invalido.");
                var count = Math.Min(Policy.EvidencePageChars, text.Length - offset);
                return ValueTask.FromResult(JsonSerializer.Serialize(new { result.ValidationId, result.EvidenceId, result.Passed,
                    observations = text.Substring(offset, count), offset, next_offset = offset + count < text.Length ? (int?)(offset + count) : null, result.ExecutionEnvironment }));
            }),
        new DelegateTool("project_validate", "Ejecuta los validadores del host para la tarea vigente. Sus resultados no los declara el modelo.",
            """{"type":"object","properties":{"work_item_id":{"type":"string"},"attempt_id":{"type":"string"}},"required":["work_item_id","attempt_id"]}""",
            async (args, token) =>
            {
                var work = args.GetProperty("work_item_id").GetString()!;
                var attempt = args.GetProperty("attempt_id").GetString()!;
                if (work != ActiveId || attempt != AttemptId || _ledger.OpenCount != (State.AllAccepted ? 1 : 2)) throw new InvalidOperationException("Intento obsoleto.");
                _bus.Publish(AgentEventKind.ValidacionSolicitada, "validator", work, new ProjectEventDataDto(1, Admitted.ContractHash, work, attempt));
                var results = await _validator.ValidateAsync(work, attempt, token);
                var batch = Guid.NewGuid().ToString("N");
                _deliveries[batch] = results;
                return JsonSerializer.Serialize(new { batch_id = batch, codigo = results.All(r => r.Passed) ? 0 : 1,
                    results = results.Select(r => new { r.ValidationId, r.Passed, r.EvidenceId, observations = Prompting.TextSanitizer.Truncate(r.Observations, Policy.MaxDefectChars) }) });
            }),
        new DelegateTool("project_command", "Ejecuta un comando admitido por el perfil del host. No admite texto de shell.",
            """{"type":"object","properties":{"profile":{"type":"string"},"work_item_id":{"type":"string"},"attempt_id":{"type":"string"}},"required":["profile","work_item_id","attempt_id"]}""",
            async (args, token) =>
            {
                if (args.GetProperty("work_item_id").GetString() != ActiveId || args.GetProperty("attempt_id").GetString() != AttemptId
                    || _ledger.OpenCount != (State.AllAccepted ? 1 : 2)) throw new InvalidOperationException("Intento/transaccion no vigente.");
                var command = _profile.Commands.SingleOrDefault(c => c.Id == args.GetProperty("profile").GetString() && c.ModelCallable)
                    ?? throw new InvalidOperationException("Comando no habilitado al modelo por el perfil.");
                var result = await _environment.RunAsync(command.Id, TimeSpan.FromSeconds(command.TimeoutSeconds), token);
                return JsonSerializer.Serialize(new { codigo = result.ExitCode, output = result.Output, cleanupConfirmed = result.CleanupConfirmed });
            }),
    ]);
}
