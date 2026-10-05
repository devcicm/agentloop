namespace AgentLoop.Goals.Projects;


public interface IProjectValidatorFactory
{
    void Bind(ProjectValidatorDefinition definition);
    IProjectValidator Create(ProjectGoalDto contract, IProjectExecutionEnvironment environment);
}
