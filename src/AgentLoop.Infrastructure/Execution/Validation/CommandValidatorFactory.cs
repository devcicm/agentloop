namespace AgentLoop.Harness.Execution;

public sealed class CommandValidatorFactory : IProjectValidatorFactory
{
    public void Bind(ProjectValidatorDefinition definition)
    {
        if (definition.CommandId is null) throw new InvalidOperationException("El validador requiere un comando admitido.");
    }
    public IProjectValidator Create(ProjectGoalDto contract, IProjectExecutionEnvironment environment) => new CommandProjectValidator(environment);
}
