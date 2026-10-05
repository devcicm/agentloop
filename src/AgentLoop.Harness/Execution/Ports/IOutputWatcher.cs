namespace AgentLoop.Terminals;
public interface IOutputWatcher
{
    string Id { get; } string Pattern { get; } string? ProcessId { get; }
    AsyncManualResetEvent Hit { get; } string? MatchedLine { get; } string? MatchedProcess { get; } bool Fired { get; }
}
