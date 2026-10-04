using AgentLoop.Goals.Projects.Contracts;
using AgentLoop.Kernel.Projects;

namespace AgentLoop.Goals.Projects;

public sealed record ValidationObservation(bool Passed, string Observations, string Environment);
public interface IProjectValidator
{
    Task<ValidationObservation> ValidateAsync(ValidationSpecDto specification,
        ProjectValidatorDefinition definition, CancellationToken token);
}

public interface IProjectValidatorFactory
{
    void Bind(ProjectValidatorDefinition definition);
    IProjectValidator Create(ProjectGoalDto contract, IProjectExecutionEnvironment environment);
}
