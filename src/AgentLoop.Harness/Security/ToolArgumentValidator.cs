using System.Text.Json;
namespace AgentLoop.Security;


/// <summary>Validated subset of the schemas emitted by this repository, not a general JSON Schema engine.</summary>
public static class ToolArgumentValidator
{
    public static string? Validate(ParsedToolCall call, JsonElement schema)
    {
        if (call.ArgumentsParseFailed) return "argumentos no validos";
        if (!ValidIdentifier(call.Id) || !ValidIdentifier(call.Name)) return "identificador de herramienta invalido";
        try
        {
            var arguments = StrictJson.Parse(call.RawArguments);
            if (arguments.ValueKind != JsonValueKind.Object) return "argumentos deben ser un objeto JSON";
            if (arguments.GetRawText() != call.Arguments.GetRawText()) return "argumentos parseados no coinciden con el payload";
            return ValidateNode(arguments, schema, "$", 0);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        { return "argumentos rechazados: " + error.Message; }
    }

    public static bool ValidIdentifier(string value)
        => value.Length is > 0 and <= 128 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    private static string? ValidateNode(JsonElement value, JsonElement schema, string path, int depth)
    {
        if (depth > DataLimits.JsonDepth) return "estructura demasiado profunda";
        if (schema.TryGetProperty("type", out var type))
        {
            var valid = type.GetString() switch
            {
                "object" => value.ValueKind == JsonValueKind.Object,
                "array" => value.ValueKind == JsonValueKind.Array,
                "string" => value.ValueKind == JsonValueKind.String,
                "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _),
                "number" => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n) && double.IsFinite(n),
                "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => value.ValueKind == JsonValueKind.Null,
                _ => false,
            };
            if (!valid) return path + ": tipo no permitido";
        }
        if (schema.TryGetProperty("enum", out var choices) && !choices.EnumerateArray().Any(c => c.ValueKind == value.ValueKind
            && (c.ValueKind == JsonValueKind.String ? c.GetString() == value.GetString() : c.GetRawText() == value.GetRawText())))
            return path + ": valor fuera del enum";
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray())
                    if (!value.TryGetProperty(name.GetString()!, out _)) return path + ": falta " + name.GetString();
            schema.TryGetProperty("properties", out var properties);
            var extraAllowed = schema.TryGetProperty("additionalProperties", out var extra) && extra.ValueKind == JsonValueKind.True;
            foreach (var property in value.EnumerateObject())
            {
                if (properties.ValueKind != JsonValueKind.Object || !properties.TryGetProperty(property.Name, out var child))
                { if (!extraAllowed) return path + ": campo desconocido " + property.Name; continue; }
                var rejection = ValidateNode(property.Value, child, path + "." + property.Name, depth + 1);
                if (rejection is not null) return rejection;
            }
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
            foreach (var element in value.EnumerateArray())
            { var rejection = ValidateNode(element, items, path + "[]", depth + 1); if (rejection is not null) return rejection; }
        if (value.ValueKind == JsonValueKind.String && schema.TryGetProperty("maxLength", out var max) && value.GetString()!.Length > max.GetInt32())
            return path + ": texto demasiado largo";
        return null;
    }
}
