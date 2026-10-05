namespace AgentLoop.Terminals;
public interface IProcessHandle
{
    string Id { get; } ShellCommand Command { get; } DateTimeOffset StartedAt { get; } DateTimeOffset? EndedAt { get; }
    int? ExitCode { get; } AsyncManualResetEvent Terminado { get; } bool Running { get; } int MaxLines { get; }
    int Pid { get; } TimeSpan Elapsed { get; } int Captured { get; } int Dropped { get; } int Unread { get; }
    IReadOnlyList<OutputLine> Tail(int count); IReadOnlyList<OutputLine> ReadNew(int max); string TailText(int count); bool Kill();
}
