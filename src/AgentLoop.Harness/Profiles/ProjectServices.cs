using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AgentLoop.Harness.Composition;


/// <summary>Explicit host composition. An agent contract cannot install profiles or command handlers.</summary>
public sealed class ProjectServices
{
    private readonly string _json;
    public IProjectResources Resources { get; }
    public string Hash { get; }
    public string SourcePath { get; }
    public string EvidenceDirectory { get; }
    public ProjectProfileDto Profile => JsonSerializer.Deserialize<ProjectProfileDto>(_json, ProjectJson.Options)!;
    public IProjectEnvironmentFactory EnvironmentFactory { get; }
    public IReadOnlyDictionary<string, IProjectValidatorFactory> Validators { get; }

    public ProjectServices(ProjectProfileDto profile, string sourcePath, string evidenceDirectory,
        IProjectEnvironmentFactory environmentFactory,
        IReadOnlyDictionary<string, IProjectValidatorFactory> validators, IProjectResources resources)
    {
        Resources = resources;
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
        foreach (var resource in EnvironmentFactory.ResourcePaths) Protect(admitted.Root, resource);
    }
    private static void Protect(string root, string resource)
    {
        if (string.IsNullOrEmpty(resource)) return;
        var relative = Path.GetRelativePath(root, Path.GetFullPath(resource));
        if (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new InvalidOperationException("El proyecto contiene recursos del host/perfil.");
    }
}
