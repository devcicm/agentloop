namespace AgentLoop.Terminals;
public interface IProcessSupervisor : IDisposable
{
    ExecutionPolicy Policy { get; } AsyncManualResetEvent Puerta { get; } int Running { get; } int Total { get; }
    IReadOnlyList<IProcessHandle> Handles { get; } IReadOnlyList<IOutputWatcher> Watchers { get; }
    IProcessHandle? Find(string id); void Pausar(); void Reanudar();
    Task<IProcessHandle> LaunchAsync(ShellCommand command, CancellationToken cancellationToken = default);
    Task<ShellResult> RunAsync(ShellCommand command, TimeSpan timeout, CancellationToken cancellationToken = default);
    IOutputWatcher Watch(string pattern, string? processId = null); bool Kill(string id); bool RemoveWatcher(string id);
}
