namespace AgentLoop.Harness.Composition;
public sealed class DefaultGoalRunSetupFactory : IGoalRunSetupFactory
{
    public OperationServices OperationServices => OperationServiceFactory.Create();
    public GoalRunSetup Build(Core.Run run, Scope scope, ILLMClient client, IReadOnlyList<IRunObserver> observers) => GoalRunFactory.BuildWithBusRecording(run,scope,client,observers);
    public AdmittedProjectContract LoadContract(string path,string workingDirectory) => ProjectContractAdmission.Load(path,workingDirectory);
}
