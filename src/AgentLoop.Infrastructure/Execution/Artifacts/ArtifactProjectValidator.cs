namespace AgentLoop.Harness.Execution;

public sealed class ArtifactProjectValidator(ProjectGoalDto contract) : IProjectValidator
{
    public async Task<ValidationObservation> ValidateAsync(ValidationSpecDto specification, ProjectValidatorDefinition definition, CancellationToken token)
    {
        foreach (var id in specification.Parameters.ArtifactIds)
        {
            var artifact = contract.Artifacts.Single(a => a.Id == id);
            var path = Path.GetFullPath(artifact.RelativePath, contract.Root);
            ProjectContractAdmission.CheckResource(path);
            if (new FileInfo(path).Length == 0) throw new InvalidOperationException("Artefacto vacio: " + id);
            if (artifact.ExpectedKind == "json") StrictJson.Parse(await File.ReadAllTextAsync(path, token));
        }
        return new(true, "Entradas presentes y estructura valida.", "artifact-host");
    }
}
