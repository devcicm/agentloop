namespace AgentLoop.StateMachines;
public interface IConversationSessionStore
{
    string Read(string path, int maxBytes);
    void Write(string path, string content);
}
