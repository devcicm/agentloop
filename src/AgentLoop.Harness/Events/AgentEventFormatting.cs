namespace AgentLoop.Events;
public static class AgentEventFormatting
{
    public static string Format(this AgentEvent item) => TextSanitizer.Truncate(DataGuard.Display($"#{item.Sequence} {item.At:HH:mm:ss} {item.Kind} [{item.Source}] {item.Text}").Replace("\n", "\\n"), 4096);
    public static string ToPrompt(this AgentEvent item) => DataGuard.Display($"- #{item.Sequence} [{item.Kind}] ({item.Source}) {item.Text}" + (item.Data is { Length: > 0 } ? $" | datos: {item.Data}" : ""));
}
