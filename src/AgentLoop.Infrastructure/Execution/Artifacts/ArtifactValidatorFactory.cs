namespace AgentLoop.Harness.Execution;


public sealed class ArtifactValidatorFactory : IProjectValidatorFactory
{
    public void Bind(ProjectValidatorDefinition definition)
    {
        if (definition.CommandId is not null) throw new InvalidOperationException("El validador de archivos no ejecuta comandos.");
    }
    public IProjectValidator Create(ProjectGoalDto contract, IProjectExecutionEnvironment environment) => new ArtifactProjectValidator(contract);
}
