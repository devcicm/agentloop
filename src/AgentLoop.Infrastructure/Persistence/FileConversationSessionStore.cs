namespace AgentLoop.StateMachines;
public sealed class FileConversationSessionStore : IConversationSessionStore
{
    public string Read(string path, int maxBytes)
    {
        if (new FileInfo(path).Length > maxBytes) throw new InvalidOperationException("La sesion supera el limite permitido.");
        return File.ReadAllText(path);
    }
    public void Write(string path, string content) => AtomicFileWriter.Write(path,content);
}
