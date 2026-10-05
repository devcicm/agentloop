namespace AgentLoop.Harness.Execution;

public sealed class CommandProjectValidator(IProjectExecutionEnvironment environment) : IProjectValidator
{
    public async Task<ValidationObservation> ValidateAsync(ValidationSpecDto specification, ProjectValidatorDefinition definition, CancellationToken token)
    {
        var result = await environment.RunAsync(definition.CommandId ?? throw new InvalidOperationException("Falta comando admitido."),
            TimeSpan.FromSeconds(specification.TimeoutSeconds), token);
        return new(result.ExitCode == 0 && result.CleanupConfirmed, result.Output, environment.Identity);
    }
}
