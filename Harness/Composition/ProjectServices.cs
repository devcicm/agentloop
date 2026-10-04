using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentLoop.Goals.Projects;
using AgentLoop.Goals.Projects.Contracts;
using AgentLoop.Harness.Execution;
using AgentLoop.Kernel.Projects;
using AgentLoop.Security;

namespace AgentLoop.Harness.Composition;

/// <summary>Explicit host composition. An agent contract cannot install profiles or command handlers.</summary>
public sealed class ProjectServices
{
    private readonly string _json;
    public string Hash { get; }
    public string SourcePath { get; }
    public string EvidenceDirectory { get; }
    public ProjectProfileDto Profile => JsonSerializer.Deserialize<ProjectProfileDto>(_json, ProjectJson.Options)!;
    public IProjectEnvironmentFactory EnvironmentFactory { get; }
    public IReadOnlyDictionary<string, IProjectValidatorFactory> Validators { get; }

    public ProjectServices(ProjectProfileDto profile, string sourcePath, string evidenceDirectory,
        IProjectEnvironmentFactory environmentFactory,
        IReadOnlyDictionary<string, IProjectValidatorFactory> validators)
    {
        _json = JsonSerializer.Serialize(profile, ProjectJson.Options);
        Hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_json)));
        SourcePath = sourcePath;
        EvidenceDirectory = Path.GetFullPath(evidenceDirectory);
        EnvironmentFactory = environmentFactory;
        Validators = new Dictionary<string, IProjectValidatorFactory>(validators, StringComparer.Ordinal);
    }

    public void Bind(AdmittedProjectContract admitted)
    {
        var profile = Profile;
        if (admitted.Contract.ExecutionProfile != profile.Id) throw new InvalidOperationException("Contrato y perfil no coinciden.");
        foreach (var validation in admitted.Contract.Validations)
        {
            var definition = profile.Validators.SingleOrDefault(v => v.Id == validation.ValidatorId)
                ?? throw new InvalidOperationException("Validador no instalado: " + validation.ValidatorId);
            if (!Validators.ContainsKey(definition.Handler)
                || definition.CommandProfile != validation.Parameters.CommandProfile || definition.Scenario != validation.Parameters.Scenario)
                throw new InvalidOperationException("Capacidad o parametros de validacion incompatibles: " + validation.Id);
            Validators[definition.Handler].Bind(definition);
        }
        Protect(admitted.Root, SourcePath);
        Protect(admitted.Root, EvidenceDirectory);
        if (profile.Execution.Docker is { } docker) Protect(admitted.Root, docker.AssetRoot);
    }
    private static void Protect(string root, string resource)
    {
        if (string.IsNullOrEmpty(resource)) return;
        var relative = Path.GetRelativePath(root, Path.GetFullPath(resource));
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("El proyecto contiene recursos del host/perfil.");
    }
}

public static class ProjectProfileLoader
{
    private static bool TimerFits(int seconds) => seconds > 0 && (double)seconds * TimeSpan.FromSeconds(1).TotalMilliseconds < uint.MaxValue;

    public static ProjectServices Load(string path, string evidenceDirectory,
        IReadOnlyDictionary<string, Func<ProjectProfileDto, IProjectEnvironmentFactory>>? adapters = null,
        IReadOnlyDictionary<string, IProjectValidatorFactory>? validatorHandlers = null)
    {
        path = Path.GetFullPath(path);
        ProjectContractAdmission.CheckResource(path);
        if (new FileInfo(path).Length > ProjectContractAdmission.MaxContractBytes) throw new IOException("Perfil excede presupuesto de admision.");
        var profile = JsonSerializer.Deserialize<ProjectProfileDto>(StrictJson.Parse(File.ReadAllText(path)).GetRawText(), ProjectJson.Options)
            ?? throw new InvalidOperationException("Perfil requerido.");
        if (profile.Version != 1 || string.IsNullOrWhiteSpace(profile.Id) || profile.Validators is null || profile.Commands is null
            || profile.Execution is null || profile.Policy is null || profile.Instructions is null)
            throw new InvalidOperationException("Perfil incompleto o version incompatible.");
        if (DataGuard.Input(profile.Instructions) != profile.Instructions) throw new InvalidOperationException("Instrucciones de perfil con controles o secretos.");
        var policy = profile.Policy;
        if (policy.MaxActionsWithoutProgress < 1 || policy.MaxExplorationReads < 0 || policy.MaxArtifactBytes < 1
            || policy.MaxEvidenceBytes < 1 || policy.MaxObservationChars < 1 || policy.MaxSnapshotObservationChars < 1
            || policy.MaxDefectChars < 1 || policy.MaxNoteChars < 1 || policy.EvidencePageChars < 1 || policy.MaxCommandOutputChars < 1
            || policy.MaxJournalBytes < 1 || policy.MaxJournalEntryBytes < 1 || policy.ContextMargin < 0 || !TimerFits(policy.StopDrainSeconds))
            throw new InvalidOperationException("Politica de perfil invalida.");
        var commands = new Dictionary<string, ProjectCommandDefinition>(StringComparer.Ordinal);
        foreach (var command in profile.Commands)
            if (command is null || string.IsNullOrWhiteSpace(command.Id) || command.Arguments is null || !TimerFits(command.TimeoutSeconds)
                || command.Arguments.Any(a => a is null || DataGuard.Input(a) != a) || !commands.TryAdd(command.Id, command))
                throw new InvalidOperationException("Comando de perfil invalido o duplicado.");
        var validators = new Dictionary<string, ProjectValidatorDefinition>(StringComparer.Ordinal);
        foreach (var validator in profile.Validators)
            if (validator is null || string.IsNullOrWhiteSpace(validator.Id) || !validators.TryAdd(validator.Id, validator)
                || validator.CommandId is not null && !commands.ContainsKey(validator.CommandId))
                throw new InvalidOperationException("Validador de perfil invalido o duplicado.");
        if (profile.Execution.Docker is not null)
        {
            var docker = profile.Execution.Docker ?? throw new InvalidOperationException("Falta configuracion Docker.");
            var directory = Path.GetDirectoryName(path)!;
            docker = docker with { Executable = Path.GetFullPath(docker.Executable, directory), AssetRoot = Path.GetFullPath(docker.AssetRoot, directory) };
            ProjectContractAdmission.CheckResource(docker.Executable);
            ProjectContractAdmission.CheckResource(docker.AssetRoot);
            if (docker.EntryPoint is null || docker.EntryPoint.Count == 0 || docker.EntryPoint.Any(string.IsNullOrWhiteSpace)
                || docker.Environment is null || docker.ResourceHashes is null || docker.ResourceHashes.Count == 0 || docker.Environment.Any(p => p.Value is null || DataGuard.Input(p.Value) != p.Value)
                || docker.Pids < 1 || !TimerFits(docker.ControlTimeoutSeconds) || !TimerFits(docker.CleanupTimeoutSeconds)
                || string.IsNullOrWhiteSpace(docker.Image) || string.IsNullOrWhiteSpace(docker.User)
                || string.IsNullOrWhiteSpace(docker.Memory) || string.IsNullOrWhiteSpace(docker.Cpus))
                throw new InvalidOperationException("Configuracion Docker incompleta.");
            profile = profile with { Execution = profile.Execution with { Docker = docker } };
            foreach (var resource in docker.ResourceHashes)
                if (string.IsNullOrWhiteSpace(resource.Key) || resource.Value is null || resource.Value.Length != 64 || resource.Value.Any(c => !char.IsAsciiHexDigit(c)))
                    throw new InvalidOperationException("Hash de recurso del perfil invalido.");
            if (!int.TryParse(docker.User.Split(':')[0], out var uid) || uid <= 0 || docker.Image.StartsWith('-')
                || new[] { docker.ProjectMount, docker.AssetMount, docker.WorkingDirectory }.Any(p => string.IsNullOrWhiteSpace(p) || !p.StartsWith('/') || p.Contains(','))
                || policy.StopDrainSeconds < 2L * docker.CleanupTimeoutSeconds)
                throw new InvalidOperationException("Aislamiento o drenaje Docker incompatible.");
        }
        var installedAdapters = adapters ?? new Dictionary<string, Func<ProjectProfileDto, IProjectEnvironmentFactory>>(StringComparer.Ordinal)
        {
            ["artifact"] = p => p.Commands.Count == 0 && p.Execution.Docker is null
                ? new ArtifactEnvironmentFactory() : throw new InvalidOperationException("Backend artifact no ejecuta comandos."),
            ["docker"] = p => new DockerEnvironmentFactory(p.Execution.Docker ?? throw new InvalidOperationException("Falta configuracion Docker."), p.Commands, p.Policy),
        };
        if (!installedAdapters.TryGetValue(profile.Execution.Adapter, out var install)) throw new InvalidOperationException("Adaptador no instalado: " + profile.Execution.Adapter);
        var factory = install(profile);
        var handlers = validatorHandlers ?? new Dictionary<string, IProjectValidatorFactory>(StringComparer.Ordinal)
        {
            ["artifact"] = new ArtifactValidatorFactory(),
            ["command"] = new CommandValidatorFactory(),
        };
        foreach (var definition in profile.Validators)
            if (!handlers.ContainsKey(definition.Handler))
                throw new InvalidOperationException("Handler incompatible: " + definition.Id);
        foreach (var definition in profile.Validators) handlers[definition.Handler].Bind(definition);
        return new(profile, path, evidenceDirectory, factory, handlers);
    }
}
