namespace AgentLoop.StateMachines;
public interface IConversationMemory
{
    IReadOnlyDictionary<string,string> Notes { get; }
    void Remember(string assignment);
    bool Forget(string key);
    string Render();
}
