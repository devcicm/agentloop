using System.Text.Json;

namespace AgentLoop.Security;


public static class StrictJson
{
    public static JsonElement Parse(string text, int maxCharacters = DataLimits.ToolArgumentsCharacters)
    {
        if (text.Length > maxCharacters) throw new JsonException("JSON exceeds its size budget.");
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = DataLimits.JsonDepth });
        RejectDuplicates(document.RootElement);
        return document.RootElement.Clone();
    }

    private static void RejectDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON property is not permitted.");
                RejectDuplicates(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) RejectDuplicates(child);
    }

    public static JsonElement CleanData(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.String) return JsonSerializer.SerializeToElement(DataGuard.Display(node.GetString()));
        if (node.ValueKind == JsonValueKind.Array) return JsonSerializer.SerializeToElement(node.EnumerateArray().Select(CleanData).ToArray());
        if (node.ValueKind != JsonValueKind.Object) return node.Clone();
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            var name = DataGuard.Display(property.Name);
            if (name != property.Name) throw new JsonException("Unsafe JSON property name.");
            fields.Add(name, CleanData(property.Value));
        }
        return JsonSerializer.SerializeToElement(fields);
    }
}
