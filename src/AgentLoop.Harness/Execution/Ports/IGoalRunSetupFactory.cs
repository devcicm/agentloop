namespace AgentLoop.Harness.Composition;
public interface IGoalRunSetupFactory
{
    GoalRunSetup Build(Core.Run run, Scope scope, ILLMClient client, IReadOnlyList<IRunObserver> observers);
    AdmittedProjectContract LoadContract(string path, string workingDirectory);
    OperationServices OperationServices { get; }
}
