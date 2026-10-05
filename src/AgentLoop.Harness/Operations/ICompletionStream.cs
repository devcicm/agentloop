namespace AgentLoop.Goals;
public interface ICompletionStream
{
    StreamDelta Add(System.Text.Json.JsonElement chunk);
    System.Text.Json.JsonElement BuildRaw();
}
