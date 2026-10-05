namespace AgentLoop.Goals.Projects;

public interface IProjectValidator
{
    Task<ValidationObservation> ValidateAsync(ValidationSpecDto specification,
        ProjectValidatorDefinition definition, CancellationToken token);
}
