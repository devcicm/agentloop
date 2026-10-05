namespace AgentLoop.Harness.Runner;


/// <summary>
/// Contrato del ejecutor de Runs. RunAsync recibe un Run y produce un
/// RunResult con su Journey. Hoy solo hay una implementacion (GoalRunner),
/// pero el interface permite que mañana un ScriptRunner o un DebugRunner
/// compartan el contrato sin que el CLI se entere.
/// </summary>
public interface IRunner
{
    Task<(RunResult Result, Journey Journey)> RunAsync(
        Core.Run run,
        RunnerOptions options,
        CancellationToken cancellationToken = default);
}
