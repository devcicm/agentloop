using System.Text.Json;

namespace AgentLoop.Harness.Functional;

public static class CanonicalJson
{
    public static string Render(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => "{"+string.Join(",",element.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>JsonSerializer.Serialize(p.Name)+":"+Render(p.Value)))+"}",
        JsonValueKind.Array => "["+string.Join(",",element.EnumerateArray().Select(Render))+"]",
        _ => element.GetRawText()
    };
}
