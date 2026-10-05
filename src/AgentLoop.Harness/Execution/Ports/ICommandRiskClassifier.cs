namespace AgentLoop.Terminals;
public interface ICommandRiskClassifier
{
    CommandRiskAssessment Assess(ShellCommand command);
}
