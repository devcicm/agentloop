using System.Security.Cryptography;
using System.Text.Json;
namespace AgentLoop.Goals.Projects;


public sealed class ProjectValidationExecutor(AdmittedProjectContract admitted, IProjectExecutionEnvironment environment, string evidenceRoot, AsyncManualResetEvent gate, ProjectServices services)
{
    private readonly ProjectProfileDto _profile = services.Profile;
    private readonly ProjectGoalDto _contract = admitted.Contract;
    public Dictionary<string, string> HashInputs(IEnumerable<string> artifactIds)
    {
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in artifactIds)
        {
            var spec = _contract.Artifacts.Single(a => a.Id == id);
            var path = Path.GetFullPath(spec.RelativePath, admitted.Root);
            services.Resources.CheckResource(path);
            hashes[id] = services.Resources.Exists(path) ? HashFile(path) : "missing";
        }
        return hashes;
    }
    private string HashFile(string path)
    {
        return services.Resources.HashFile(path, _profile.Policy.MaxArtifactBytes);
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
            var observedEnvironment = environment.Identity;
            var observations = passed ? "Entradas presentes." : "Faltan artefactos de entrada.";
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
            budget.CancelAfter(TimeSpan.FromSeconds(spec.TimeoutSeconds));
            try
            {
                if (passed)
                {
                    var definition = _profile.Validators.Single(v => v.Id == spec.ValidatorId);
                    var handler = services.Validators[definition.Handler].Create(_contract, environment);
                    var observation = await handler.ValidateAsync(spec, definition, budget.Token);
                    passed = observation.Passed;
                    observations = observation.Observations;
                    observedEnvironment = observation.Environment;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
            { passed = false; observations = ex.Message; }
            var after = HashInputs(spec.Parameters.ArtifactIds);
            passed &= before.All(pair => after[pair.Key] == pair.Value);
            var result = new ValidationResultDto(spec.Id, attemptId, admitted.ContractHash, before, passed,
                Prompting.TextSanitizer.Truncate(DataGuard.Display(observations), _profile.Policy.MaxObservationChars), Guid.NewGuid().ToString("N"), started, DateTimeOffset.UtcNow, ExecutionEnvironment: observedEnvironment, ProfileHash: services.Hash);
            services.Resources.CreateDirectory(evidenceRoot);
            var payload = JsonSerializer.Serialize(result, ProjectJson.Options);
            if (System.Text.Encoding.UTF8.GetByteCount(payload) > _profile.Policy.MaxEvidenceBytes)
                throw new IOException("Registro de evidencia excede politica; no se declara validacion exitosa.");
            await services.Resources.WriteTextAsync(Path.Combine(evidenceRoot, result.EvidenceId + ".json"), payload, token);
            results.Add(result);
        }
        return results;
    }
}
