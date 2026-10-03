using System.Security.Cryptography;
using System.Text.Json;
using AgentLoop.Goals.Projects.Contracts;
using AgentLoop.Harness.Execution;
using AgentLoop.Security;
using AgentLoop.Concurrency;

namespace AgentLoop.Goals.Projects;

public sealed class ProjectValidationExecutor(AdmittedProjectContract admitted, IProjectExecutionEnvironment environment, string evidenceRoot, AsyncManualResetEvent gate)
{
    private readonly ProjectGoalDto _contract = admitted.Contract;
    public Dictionary<string, string> HashInputs(IEnumerable<string> artifactIds)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in artifactIds)
        {
            var spec = _contract.Artifacts.Single(a => a.Id == id);
            var path = Path.GetFullPath(spec.RelativePath, admitted.Root);
            ProjectContractAdmission.CheckResource(path);
            hashes[id] = File.Exists(path) ? HashFile(path) : "missing";
        }
        return hashes;
    }
    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 16 * 1024 * 1024) throw new IOException("Artefacto excede 16 MiB.");
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public async Task<IReadOnlyList<ValidationResultDto>> ValidateAsync(string workItemId, string attemptId, CancellationToken token)
    {
        var specs = workItemId == "__goal__" ? _contract.Validations.Where(v => v.Required).ToArray()
            : _contract.Validations.Where(v => v.WorkItemId == workItemId).ToArray();
        var results = new List<ValidationResultDto>();
        // Iteration over a bounded validation batch; execution scheduling remains event driven.
        foreach (var spec in specs)
        {
            await gate.WaitAsync(token);
            var started = DateTimeOffset.UtcNow;
            var before = HashInputs(spec.Parameters.ArtifactIds);
            var passed = !before.Values.Contains("missing");
            var observations = passed ? "Entradas presentes." : "Faltan artefactos de entrada.";
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(spec.TimeoutSeconds));
            try
            {
                if (passed && spec.ValidatorId == "artifact")
                    foreach (var id in spec.Parameters.ArtifactIds)
                    {
                        var artifact = _contract.Artifacts.Single(a => a.Id == id);
                        var path = Path.GetFullPath(artifact.RelativePath, admitted.Root);
                        if (new FileInfo(path).Length == 0) throw new InvalidOperationException("Artefacto vacio: " + id);
                        if (artifact.ExpectedKind == "json") StrictJson.Parse(await File.ReadAllTextAsync(path, budget.Token));
                    }
                else if (passed)
                {
                    var profile = spec.ValidatorId switch { "build" => "build", "process-probe" => "health", "pos-functional" => "pos", _ => throw new InvalidOperationException("Validador no registrado.") };
                    var command = await environment.RunAsync(profile, TimeSpan.FromSeconds(spec.TimeoutSeconds), budget.Token);
                    passed = command.ExitCode == 0 && command.CleanupConfirmed;
                    observations = command.Output;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            { passed = false; observations = ex.Message; }
            var after = HashInputs(spec.Parameters.ArtifactIds);
            passed &= before.All(pair => after[pair.Key] == pair.Value);
            var result = new ValidationResultDto(spec.Id, attemptId, admitted.ContractHash, before, passed,
                Prompting.TextSanitizer.Truncate(DataGuard.Display(observations), 4000), Guid.NewGuid().ToString("N"), started, DateTimeOffset.UtcNow, ExecutionEnvironment: spec.ValidatorId == "artifact" ? "artifact-host" : environment.Identity);
            Directory.CreateDirectory(evidenceRoot);
            await File.WriteAllTextAsync(Path.Combine(evidenceRoot, result.EvidenceId + ".json"),
                JsonSerializer.Serialize(result, ProjectJson.Options), token);
            results.Add(result);
        }
        return results;
    }
}
