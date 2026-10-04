using System.Text.Json;
using System.Text.Json.Nodes;
using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Goals.Projects.Contracts;
using AgentLoop.Harness;
using AgentLoop.Harness.Context;
using AgentLoop.Harness.Execution;
using AgentLoop.Harness.Composition;
using AgentLoop.Kernel.Projects;
using AgentLoop.Provider;
using AgentLoop.Terminals;
using AgentLoop.Tools;

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
        public Task PreflightAsync(CancellationToken token) => Task.CompletedTask;
        public Task<ProjectCommandResult> RunAsync(string profile, TimeSpan timeout, CancellationToken token)
        { Calls++; if (Calls == 1) FirstCommand?.Invoke(); return Task.FromResult(new ProjectCommandResult(0, "fixture de mecanismo, sin aislamiento ni oraculo real", true)); }
    }

    public static async Task<bool> RunAsync(CancellationToken token)
    {
        var checks = new List<(string Name, bool Ok)>();
        void Check(string name, bool ok) => checks.Add((name, ok));
        var repo = Configuration.SecretStore.ResolveRepositoryRoot();
        var root = Path.Combine(Path.GetTempPath(), "agentloop-project-" + Guid.NewGuid().ToString("N"));
        var example = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(repo, "examples", "project-goals", "minimal.example.json"), token))!;
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
        var services = new ProjectServices(profile, baseServices.SourcePath, baseServices.EvidenceDirectory, baseServices.EnvironmentFactory, baseServices.Validators);
        var admitted = ProjectContractAdmission.Admit(example.ToJsonString(), repo);
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

        var bus = new EventBus { RunId = Guid.NewGuid(), AsyncObservers = true, DedupWindow = TimeSpan.Zero };
        var policy = new ExecutionPolicy { WorkingDirectory = root, AllowedPaths = [root], AllowShell = false };
        using var supervisor = new ProcessSupervisor(bus, policy);
        var ledger = new CardLedger(CardDeck.Transaccional) { WorkingDirectory = root };
        var environment = new FakeEnvironment();
        var runtime = new ProjectRuntime(admitted, ledger, bus, services, environment, supervisor.Puerta);
        var scope = Scope.Minimal(policy);
        var tools = OperationTools.Build(ledger, supervisor, bus).Combine(BuiltInTools.DefaultRegistry()).Combine(runtime.Tools()).Filter(n => n is "tarjeta" or "write_text_file" or "project_validate" or "project_command").WithScope(scope);
        var context = new GoalContext { Goal = Goal.Of(copy.Objective, copy.Budgets.DeadlineSeconds), Bus = bus, Ledger = ledger, Supervisor = supervisor, Project = runtime };
        var client = new ProjectScriptClient(admitted, Path.Combine(repo, "examples", "fixtures", "minimal"), profile.Policy.MaxArtifactBytes);
        var engine = new EventOperationCoordinator(client, "fixture", tools, scope) { JournalDirectory = Path.Combine(root, "journal"), MaxModelCalls = 100 };
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
