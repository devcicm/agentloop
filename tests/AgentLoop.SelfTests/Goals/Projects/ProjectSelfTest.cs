using System.Text.Json;
using System.Text.Json.Nodes;
namespace AgentLoop.Goals.Projects;


public static class ProjectSelfTest
{
    private sealed class FakeEnvironment : IProjectExecutionEnvironment
    {
        public int Running => 0;
        public string Identity => "simulated-backend";
        public Task CheckPreviousRunAsync(Guid runId, CancellationToken token) => Task.CompletedTask;
        public int Calls { get; private set; }
        public Action? FirstCommand { get; init; }
        public int? FailOnCall { get; init; }
        public Task PreflightAsync(CancellationToken token) => Task.CompletedTask;
        public Task<ProjectCommandResult> RunAsync(string profile, TimeSpan timeout, CancellationToken token)
        { Calls++; if (Calls == 1) FirstCommand?.Invoke(); return Task.FromResult(new ProjectCommandResult(Calls == FailOnCall ? 1 : 0, "fixture de mecanismo, sin aislamiento ni oraculo real", true)); }
    }

    public static async Task<bool> RunAsync(CancellationToken token)
    {
        var checks = new List<(string Name, bool Ok)>();
        void Check(string name, bool ok) => checks.Add((name, ok));
        var repo = Configuration.SecretStore.ResolveRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "agentloop-project-" + Guid.NewGuid().ToString("N"));
        var example = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(repo, "examples", "contracts", "minimal.example.json"), token))!;
        example["root"] = root;
        var baseServices = ProjectProfileLoader.Load(Path.Combine(repo, "examples", "profiles", "artifacts.example.json"), Path.Combine(repo, ".agentloop", "project-evidence"));
        var profile = baseServices.Profile with
        {
            Validators = [..baseServices.Profile.Validators, new ProjectValidatorDefinition("external-check", "command", "check")],
            Commands = [new ProjectCommandDefinition("check", [], false, 30)],
        };
        foreach (var id in new[] { "check-one", "check-two" })
        {
            example["validations"]!.AsArray().Add(JsonNode.Parse(JsonSerializer.Serialize(new AgentLoop.Goals.Projects.Contracts.ValidationSpecDto
            { Id = id, ValidatorId = "external-check", WorkItemId = "delivery", Parameters = new() { ArtifactIds = ["delivery"] }, Required = true, TimeoutSeconds = 30 }, ProjectJson.Options)));
            example["workItems"]![1]!["validationIds"]!.AsArray().Add(id);
        }
        var services = new ProjectServices(profile, baseServices.SourcePath, baseServices.EvidenceDirectory, baseServices.EnvironmentFactory, baseServices.Validators, baseServices.Resources);
        var admitted = ProjectContractAdmission.Admit(example.ToJsonString(), repo);
        var invalidExecutable = Path.Combine(Path.GetTempPath(), "agentloop-invalid-backend-" + Guid.NewGuid().ToString("N") + ".exe");
        await File.WriteAllTextAsync(invalidExecutable, "not an executable", token);
        try
        {
            var invalidDefinition = new DockerExecutionDefinition(invalidExecutable, "unused", Path.GetTempPath(), ["unused"], "/project", "/profile",
                "1000:1000", "128m", "1", 16, "/tmp", "/tmp", new Dictionary<string, string>(), new Dictionary<string, string>(), 2, 2);
            await new DockerProjectEnvironment(root, Guid.NewGuid(), invalidDefinition, [], profile.Policy).PreflightAsync(token);
            Check("backend no ejecutable falla como error controlado", false);
        }
        catch (IOException) { Check("backend no ejecutable falla como error controlado", true); }
        finally { File.Delete(invalidExecutable); }
        Check("admision sin crear raiz", !Directory.Exists(root));
        var copy = admitted.Contract;
        Check("hash estable de revision admitida", ProjectContractAdmission.Admit(admitted.CanonicalJson, repo).ContractHash == admitted.ContractHash);
        void Reject(string name, Action<JsonNode> change)
        {
            var node = example.DeepClone(); change(node);
            try { services.Bind(ProjectContractAdmission.Admit(node.ToJsonString(), repo)); Check(name, false); }
            catch (Exception ex) when (ex is InvalidOperationException or JsonException or ArgumentException) { Check(name, true); }
        }
        Reject("rechaza campo desconocido", n => n["extra"] = "poison");
        Reject("rechaza version desconocida", n => n["version"] = 999);
        Reject("rechaza ciclo", n => n["workItems"]![0]!["dependsOn"] = new JsonArray("delivery"));
        Reject("rechaza ruta fuera de raiz", n => n["artifacts"]![0]!["relativePath"] = "../escape.txt");
        Reject("rechaza validador desconocido", n => n["validations"]![0]!["validatorId"] = "shell");
        Reject("rechaza shell arbitrario en parametros", n => n["validations"]![0]!["parameters"]!["command"] = "echo poison");
        Reject("rechaza presupuestos invalidos", n => n["budgets"]!["maxModelCalls"] = 0);
        Reject("rechaza referencia inexistente", n => n["workItems"]![0]!["validationIds"] = new JsonArray("missing"));
        try { ProjectContractAdmission.Admit(example.ToJsonString().Replace("\"version\":1", "\"version\":1,\"version\":1"), repo); Check("rechaza claves duplicadas", false); }
        catch (JsonException) { Check("rechaza claves duplicadas", true); }

        foreach (var forbidden in new[] { repo, Path.Combine(repo, "Kernel") })
        {
            try { ProjectContractAdmission.CheckExecutionRoot(forbidden); Check("root protege " + Path.GetFileName(forbidden), false); }
            catch (InvalidOperationException) { Check("root protege " + Path.GetFileName(forbidden), true); }
        }
        try { JsonSerializer.Deserialize<ProjectSnapshotDto>("{}", ProjectJson.Options); Check("snapshot exige campos requeridos", false); }
        catch (JsonException) { Check("snapshot exige campos requeridos", true); }

        var state = new ProjectState(copy);
        Check("habilita primera tarea por dependencias", state.EnableNext()!.Id == "definition");
        state.AcceptCurrent();
        Check("cerrar tarea no cumple proyecto", !state.AllAccepted && state.EnableNext()!.Id == "delivery");

        var compact = ProjectContextBudget.Fit(new ChatRequest { Model = "fixture", Messages = [ChatMessage.FromSystem("sistema"), ChatMessage.FromUser("viejo"), ChatMessage.FromAssistant(new string('x', 5000)), ChatMessage.FromUser("vigente")] }, 1000);
        Check("poda intercambios completos conserva sistema y tarea", compact.Messages.Count == 2 && compact.Messages[^1].Content == "vigente");
        try { ProjectContextBudget.Fit(new ChatRequest { Model = "fixture", Messages = [ChatMessage.FromSystem(new string('x', 5000)), ChatMessage.FromUser("actual")] }, 1000); Check("contrato minimo imposible falla", false); }
        catch (InvalidOperationException) { Check("contrato minimo imposible falla", true); }

        var knowledge = new ProjectKnowledge();
        Check("primera lectura consume exploracion", knowledge.ObserveRead("definition", "hash-one", 2));
        knowledge.Note("definition", "hash-one", "hallazgo sin autoridad");
        Check("relectura no renueva exploracion", !knowledge.ObserveRead("definition", "hash-one", 2));
        knowledge.RecordAction(false, false);
        var restoredKnowledge = new ProjectKnowledge();
        restoredKnowledge.Restore(knowledge.Snapshot(), new HashSet<string> { "definition", "delivery" });
        Check("snapshot conserva notas lecturas y presupuesto", restoredKnowledge.Artifacts.Single().Reads == 2
            && restoredKnowledge.Artifacts.Single().Note == "hallazgo sin autoridad" && restoredKnowledge.ActionsWithoutProgress == 1);
        Check("recuperacion no permite renovar lectura previa", !restoredKnowledge.ObserveRead("definition", "hash-one", 2));
        Check("hash nuevo elimina nota anterior", restoredKnowledge.ObserveRead("definition", "hash-two", 2)
            && restoredKnowledge.Artifacts.Single().Note is null);
        Check("exploracion agotada no habilita lecturas libres", !restoredKnowledge.ObserveRead("delivery", "hash-three", 2));
        restoredKnowledge.RecordAction(true, false);
        Check("progreso material reinicia contador", restoredKnowledge.ActionsWithoutProgress == 0);
        restoredKnowledge.BeginWork();
        Check("nueva tarea preserva conocimiento reinicia exploracion", restoredKnowledge.Artifacts.Count == 2 && restoredKnowledge.ExplorationReads == 0);
        try { restoredKnowledge.Restore(knowledge.Snapshot() with { ExplorationReads = 99 }, new HashSet<string> { "definition" }); Check("rechaza presupuesto recuperado inconsistente", false); }
        catch (InvalidOperationException) { Check("rechaza presupuesto recuperado inconsistente", true); }

        var repair = new ProjectState(copy);
        repair.EnableNext(); repair.AcceptCurrent(); repair.EnableNext(); repair.AcceptCurrent();
        repair.RecordValidationFailure("delivery");
        repair.Invalidate(["definition"]);
        Check("integracion invalida productor y dependencias", repair.Snapshot.All(w => w.Status == ProjectWorkStatus.Pending)
            && repair.Snapshot.Single(w => w.Id == "delivery").Corrections == 1 && repair.EnableNext()!.Id == "definition");

        var bus = new EventBus { RunId = Guid.NewGuid(), AsyncObservers = true, DedupWindow = TimeSpan.Zero };
        var policy = new ExecutionPolicy { WorkingDirectory = root, AllowedPaths = [root], AllowShell = false };
        using var supervisor = new ProcessSupervisor(bus, policy);
        var ledger = new CardLedger(CardDeck.Transaccional, new EvidenceManifest()) { WorkingDirectory = root };
        var environment = new FakeEnvironment();
        var runtime = new ProjectRuntime(admitted, ledger, bus, services, environment, supervisor.Puerta);
        var scope = Scope.Minimal(policy);
        var tools = OperationTools.Build(ledger, supervisor, bus).Combine(BuiltInTools.DefaultRegistry()).Combine(runtime.Tools()).Filter(n => n is "tarjeta" or "write_text_file" or "project_validate" or "project_command").WithScope(scope);
        var context = new GoalContext { Goal = Goal.Of(copy.Objective, copy.Budgets.DeadlineSeconds), Bus = bus, Ledger = ledger, Supervisor = supervisor, Project = runtime };
        var client = new ProjectFixtureClient(admitted, Path.Combine(repo, "examples", "fixtures", "minimal"), profile.Policy.MaxArtifactBytes);
        var engine = new EventOperationCoordinator(client, "fixture", tools, scope, OperationServiceFactory.Create()) { JournalDirectory = Path.Combine(root, "journal"), MaxModelCalls = 100 };
        var operation = await engine.RunAsync(context, token);
        Check("actor de proyecto cierra raiz con entregables verificados", operation.IsSuccess && runtime.State.AllAccepted && ledger.GoalMet);
        Check("validacion integrada se ejecuta tras las tareas", environment.Calls == 4);
        Check("eventos de trabajo y validacion en el mismo bus", bus.History.Any(e => e.Kind == AgentEventKind.TrabajoAceptado) && bus.History.Any(e => e.Kind == AgentEventKind.ValidacionFinalizada));
        Check("tarjetas identifican tareas e intentos", ledger.Plays.Where(p => p.Accepted && p.Kind() != CardKind.Apertura).All(p => p.Fields.ContainsKey("work_item_id")));
        var recovered = ProjectRecovery.Inspect(engine.JournalPath, admitted);
        Check("snapshot durable compatible sin acciones inciertas", recovered.Terminal && recovered.UncertainActions.Count == 0 && recovered.Snapshot.Work.All(w => w.Status == ProjectWorkStatus.Accepted));
        var changed = Path.Combine(root, copy.Artifacts[0].RelativePath);
        await File.AppendAllTextAsync(changed, " ", token);
        Check("hash cambiado invalida cierre", runtime.CheckCard(CardKind.Comprobacion, new Dictionary<string,string> { ["work_item_id"] = "__goal__", ["attempt_id"] = "integration" }) is not null);
        Check("checkpoint atomico disponible", File.Exists(engine.JournalPath + ".checkpoint.json"));
        var pageResult = recovered.Snapshot.Results.First();
        var evidencePath = Path.Combine(services.EvidenceDirectory, bus.RunId.ToString("N"), pageResult.EvidenceId + ".json");
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(pageResult with { Observations = new string('d', profile.Policy.EvidencePageChars + 17) }, ProjectJson.Options), token);
        async Task<ToolExecution> Evidence(object arguments) => await runtime.Tools().ExecuteAsync(
            new AgentLoop.Parsing.ParsedToolCall(Guid.NewGuid().ToString("N"), "project_evidence", JsonSerializer.SerializeToElement(arguments), JsonSerializer.Serialize(arguments), false), token);
        var firstPage = await Evidence(new { evidence_id = pageResult.EvidenceId });
        using var firstPageJson = JsonDocument.Parse(firstPage.Output);
        var nextOffset = firstPageJson.RootElement.GetProperty("next_offset").GetInt32();
        var lastPage = await Evidence(new { evidence_id = pageResult.EvidenceId, offset = nextOffset });
        using var lastPageJson = JsonDocument.Parse(lastPage.Output);
        Check("evidencia paginada no pierde diagnostico", !firstPage.Failed && !lastPage.Failed
            && nextOffset == profile.Policy.EvidencePageChars && lastPageJson.RootElement.GetProperty("observations").GetString()!.Length == 17
            && lastPageJson.RootElement.GetProperty("next_offset").ValueKind == JsonValueKind.Null);
        Check("evidencia rechaza offset fuera de rango", (await Evidence(new { evidence_id = pageResult.EvidenceId, offset = int.MaxValue })).Failed);
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(pageResult with { ProfileHash = "other-profile" }, ProjectJson.Options), token);
        Check("evidencia rechaza perfil alterado", (await Evidence(new { evidence_id = pageResult.EvidenceId })).Failed);
        await File.WriteAllTextAsync(evidencePath, JsonSerializer.Serialize(pageResult, ProjectJson.Options), token);
        var repairRoot = Path.Combine(root, "repair-project");
        var repairContractNode = example.DeepClone(); repairContractNode["root"] = repairRoot;
        var repairContract = ProjectContractAdmission.Admit(repairContractNode.ToJsonString(), repo);
        var repairBus = new EventBus { RunId = Guid.NewGuid(), AsyncObservers = true, DedupWindow = TimeSpan.Zero };
        var repairPolicy = new ExecutionPolicy { WorkingDirectory = repairRoot, AllowedPaths = [repairRoot], AllowShell = false };
        using var repairSupervisor = new ProcessSupervisor(repairBus, repairPolicy);
        var repairLedger = new CardLedger(CardDeck.Transaccional, new EvidenceManifest()) { WorkingDirectory = repairRoot };
        var repairEnvironment = new FakeEnvironment { FailOnCall = 3 };
        var repairRuntime = new ProjectRuntime(repairContract, repairLedger, repairBus, services, repairEnvironment, repairSupervisor.Puerta);
        var repairScope = Scope.Minimal(repairPolicy);
        var repairTools = OperationTools.Build(repairLedger, repairSupervisor, repairBus).Combine(BuiltInTools.DefaultRegistry()).Combine(repairRuntime.Tools())
            .Filter(n => n is "tarjeta" or "write_text_file" or "project_validate").WithScope(repairScope);
        var repairContext = new GoalContext { Goal = Goal.Of(copy.Objective, copy.Budgets.DeadlineSeconds), Bus = repairBus, Ledger = repairLedger, Supervisor = repairSupervisor, Project = repairRuntime };
        var repairEngine = new EventOperationCoordinator(new ProjectFixtureClient(repairContract, Path.Combine(repo, "examples", "fixtures", "minimal"), profile.Policy.MaxArtifactBytes), "fixture", repairTools, repairScope, OperationServiceFactory.Create())
            { JournalDirectory = Path.Combine(root, "repair-journal"), MaxModelCalls = 100 };
        var repaired = await repairEngine.RunAsync(repairContext, token);
        Check("fallo de integracion habilita reparacion por eventos y cierre", repaired.IsSuccess && repairEnvironment.Calls == 8
            && repairRuntime.State.Snapshot.Single(w => w.Id == "delivery").Attempts == 2
            && repairRuntime.State.Snapshot.Single(w => w.Id == "delivery").Corrections == 1
            && repairBus.History.Any(e => e.Kind == AgentEventKind.ValidacionFallida));
        var validationGate = new AgentLoop.Concurrency.AsyncManualResetEvent(signaled: true);
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gatedEnvironment = new FakeEnvironment { FirstCommand = () => { validationGate.Reset(); blocked.TrySetResult(); } };
        var gatedValidator = new ProjectValidationExecutor(admitted, gatedEnvironment, Path.Combine(root, "gate-evidence"), validationGate, services);
        var batchTask = gatedValidator.ValidateAsync("delivery", "gate-test", token);
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        Check("pausa entre validadores no inicia comando siguiente", !batchTask.IsCompleted && gatedEnvironment.Calls == 1);
        validationGate.Set();
        var gatedResults = await batchTask;
        Check("senal manual reanuda batch sin polling", gatedEnvironment.Calls == 2 && gatedResults.All(r => r.Passed));
        var uncertainPath = Path.Combine(root, "uncertain.jsonl");
        File.Copy(engine.JournalPath, uncertainPath);
        var uncertainId = Guid.NewGuid().ToString("N");
        var uncertainEvent = new AgentEvent(99999, DateTimeOffset.UtcNow, AgentEventKind.HerramientaSolicitada, "coordinator", "admision", JsonSerializer.Serialize(new { action_id = uncertainId, tool = "write_text_file" })) { RunId = bus.RunId };
        await File.AppendAllTextAsync(uncertainPath, JsonSerializer.Serialize(new AgentLoop.Harness.Durable.JournalEntry("admitted", uncertainEvent, DateTimeOffset.UtcNow)) + "\n", token);
        Check("detecta efecto admitido sin resolver", ProjectRecovery.Inspect(uncertainPath, admitted).UncertainActions.Contains(uncertainId));
        try { ProjectRecovery.Load(uncertainPath, admitted); Check("no reejecuta efectos inciertos", false); }
        catch (InvalidOperationException) { Check("no reejecuta efectos inciertos", true); }
        ProjectRecovery.Reconcile(uncertainPath, admitted, uncertainId, "applied", "Artefactos inspeccionados por operador de prueba");
        Check("reconciliacion explicita desbloquea reconstruccion", ProjectRecovery.Load(uncertainPath, admitted).ContractHash == admitted.ContractHash);
        var tornPath = Path.Combine(root, "torn.jsonl");
        File.Copy(engine.JournalPath, tornPath); await File.AppendAllTextAsync(tornPath, "{partial", token);
        Check("conserva registros antes de escritura final truncada", ProjectRecovery.Inspect(tornPath, admitted).Terminal);
        var corruptPath = Path.Combine(root, "corrupt.jsonl");
        await File.WriteAllTextAsync(corruptPath, "{broken}\n" + await File.ReadAllTextAsync(engine.JournalPath, token), token);
        try { ProjectRecovery.Inspect(corruptPath, admitted); Check("corrupcion intermedia bloquea recuperacion", false); }
        catch (InvalidOperationException) { Check("corrupcion intermedia bloquea recuperacion", true); }
        state.RecordValidationFailure(); state.RecordValidationFailure(); state.RecordValidationFailure();
        try { state.RecordValidationFailure(); Check("presupuesto de correcciones por tarea", false); }
        catch (InvalidOperationException) { Check("presupuesto de correcciones por tarea", true); }
        foreach (var item in checks) Console.WriteLine($"{(item.Ok ? "OK" : "FAIL")} project: {item.Name}");
        Console.WriteLine($"PROJECT SELF-TEST {(checks.All(c => c.Ok) ? "OK" : "FAIL")}: {checks.Count} comprobaciones; backend simulado, sin red.");
        return checks.All(c => c.Ok);
    }
    private static CardKind? Kind(this CardPlay play) => play.Card?.Kind;
}
