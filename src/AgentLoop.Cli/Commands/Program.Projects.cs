using System.Text.Json;
namespace AgentLoop;

internal static partial class Program
{
    private static int ProjectAdmissionCommand(string[] args)
    {
        try
        {
            var file = StringOption(args, "--project-contract");
            if (string.IsNullOrWhiteSpace(file) || file.StartsWith("--", StringComparison.Ordinal))
                throw new InvalidOperationException("Uso: project-admit --project-contract <archivo.json> [--cwd <directorio>].");
            var admitted = ProjectContractAdmission.Load(file, StringOption(args, "--cwd") ?? Environment.CurrentDirectory);
            var services = StringOption(args, "--project-profile") is null ? null : LoadProjectServices(args, admitted);
            var contract = admitted.Contract;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "admitted", executionRequires = "Perfil instalado, capacidades vinculadas y preflight del adaptador",
                contractHash = admitted.ContractHash, profileHash = services?.Hash, capabilitiesBound = services is not null, contract,
            }, new JsonSerializerOptions(AgentLoop.Goals.Projects.Contracts.ProjectJson.Options) { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException
            or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.Error.WriteLine(DataGuard.Display("ERROR: " + ex.Message));
            return 64;
        }
    }

    private static AdmittedProjectContract LoadProjectContract(string[] args)
    {
        var path = StringOption(args, "--project-contract");
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith("--", StringComparison.Ordinal))
            throw new InvalidOperationException("Falta --project-contract <archivo.json>.");
        return ProjectContractAdmission.Load(path, StringOption(args, "--cwd") ?? Environment.CurrentDirectory);
    }

    private static int ProjectRecoveryCommand(string[] args, string command)
    {
        try
        {
            var contract = LoadProjectContract(args);
            var journal = Path.GetFullPath(StringOption(args, "--resume-journal") ?? throw new InvalidOperationException("Falta --resume-journal <jsonl>."));
            var policy = StringOption(args, "--project-profile") is not null ? LoadProjectServices(args, contract).Profile.Policy : null;
            var recoveryPolicy = policy is null ? null : new AgentLoop.Kernel.Projects.ProjectRecoveryPolicy(policy.MaxJournalBytes, policy.MaxJournalEntryBytes);
            if (command == "project-reconcile")
                ProjectRecovery.Reconcile(journal, contract,
                    StringOption(args, "--action-id") ?? "", StringOption(args, "--decision") ?? "",
                    StringOption(args, "--note") ?? "", recoveryPolicy);
            Console.WriteLine(JsonSerializer.Serialize(ProjectRecovery.Inspect(journal, contract, recoveryPolicy), new JsonSerializerOptions(AgentLoop.Goals.Projects.Contracts.ProjectJson.Options) { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { Console.Error.WriteLine(DataGuard.Display(ex.Message)); return 64; }
    }

    private static async Task<int> ProjectBackendCheckAsync(string[] args, CancellationToken token)
    {
        try
        {
            var contract = LoadProjectContract(args);
            if (!Directory.Exists(contract.Root)) throw new InvalidOperationException("El chequeo de montajes requiere una raiz existente; project-run crea la raiz despues del preflight.");
            var services = LoadProjectServices(args, contract);
            var environment = services.EnvironmentFactory.Create(contract.Root, Guid.NewGuid());
            await environment.PreflightAsync(token);
            var commandId = StringOption(args, "--command");
            if (commandId is null)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { environment = environment.Identity, profileHash = services.Hash, preflightOnly = true }));
                return 0;
            }
            var command = services.Profile.Commands.SingleOrDefault(c => c.Id == commandId) ?? throw new InvalidOperationException("Comando no registrado por el host.");
            var result = await environment.RunAsync(command.Id, TimeSpan.FromSeconds(command.TimeoutSeconds), token);
            Console.WriteLine(JsonSerializer.Serialize(new { environment = environment.Identity, profileHash = services.Hash, result }));
            return result.ExitCode == 0 && result.CleanupConfirmed ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException or UnauthorizedAccessException or ArgumentException)
        { Console.Error.WriteLine(DataGuard.Display(ex.Message)); return 64; }
    }

    internal static async Task<int> ProjectRunAsync(string[] args, CancellationToken token, ILLMClient? suppliedClient = null)
    {
        if (HasFlag(args, "--fixture") && suppliedClient is null) return await VerificationCommandAsync(args, token);
        try
        {
            var contract = LoadProjectContract(args);
            var resume = StringOption(args, "--resume-journal");
            var services = LoadProjectServices(args, contract);
            if (resume is not null) ProjectRecovery.Load(Path.GetFullPath(resume), contract, new(services.Profile.Policy.MaxJournalBytes, services.Profile.Policy.MaxJournalEntryBytes));
            ProjectContractAdmission.CheckExecutionRoot(contract.Root);
            await services.EnvironmentFactory.Create(contract.Root, Guid.NewGuid()).PreflightAsync(token);
            var spec = contract.Contract;
            var model = suppliedClient is not null ? "fixture" : ResolveModel(args);
            var thinking = ResolveThinkingPolicy(args);
            using var clientOwner = suppliedClient is not null ? null : BuildClient(out _, thinking);
            ILLMClient client = suppliedClient ?? clientOwner!;
            var run = Harness.Core.Run.FromObjective(spec.Objective, spec.Budgets.DeadlineSeconds, model)
                with { ProjectContract = contract, ProjectServices = services, ResumeJournal = resume is null ? null : Path.GetFullPath(resume) };
            var options = new Harness.Runner.RunnerOptions
            {
                WorkingDirectory = contract.Root, AllowShell = false, AllowedPaths = [contract.Root],
                DeadlineSeconds = spec.Budgets.DeadlineSeconds,
                MaxIterations = Math.Min(IntOption(args, "--max-ciclos", spec.Budgets.MaxModelCalls), spec.Budgets.MaxModelCalls),
                MaxTokens = Math.Min(IntOption(args, "--max-tokens", spec.Budgets.MaxOutputTokens), spec.Budgets.MaxOutputTokens),
                MaxConcurrent = spec.Budgets.MaxConcurrentProcesses, Thinking = thinking, Verbose = true,
            };
            var store = new Harness.Durable.FileRunStore(Path.Combine(SecretStore.ResolveRepositoryRoot(), "artifacts", "journeys"));
            var (result, journey) = await Harness.Composition.GoalRunFactory.RunWithRunnerAsync(run, options, client, store, token);
            Console.WriteLine(DataGuard.Display(JsonSerializer.Serialize(new { runId = run.Id, contractHash = contract.ContractHash,
                status = journey.FinalStatus.ToString(), outcome = journey.Outcome,
                journal = Path.Combine(SecretStore.ResolveRepositoryRoot(), ".agentloop", "event-journal", run.Id.ToString("N") + ".jsonl") })));
            return result.IsSuccess ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { Console.Error.WriteLine(DataGuard.Display("ERROR proyecto: " + ex.Message)); return 64; }
    }

    private static Harness.Composition.ProjectServices LoadProjectServices(string[] args, AdmittedProjectContract contract)
    {
        var cwd = Path.GetFullPath(StringOption(args, "--cwd") ?? Environment.CurrentDirectory);
        var source = StringOption(args, "--project-profile") ?? throw new InvalidOperationException("Falta --project-profile <perfil externo del host>. No hay perfil implicito.");
        var evidence = Path.GetFullPath(StringOption(args, "--evidence-dir") ?? Path.Combine(SecretStore.ResolveRepositoryRoot(), ".agentloop", "project-evidence"), cwd);
        var services = Harness.Composition.ProjectProfileLoader.Load(Path.GetFullPath(source, cwd), evidence);
        services.Bind(contract);
        return services;
    }

}
