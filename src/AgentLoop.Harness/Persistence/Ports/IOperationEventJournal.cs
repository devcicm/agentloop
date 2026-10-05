namespace AgentLoop.Harness.Durable;
public interface IOperationEventJournal : IDisposable
{
    string Path { get; }
    void Append(string phase, AgentEvent item, object? state = null);
}
