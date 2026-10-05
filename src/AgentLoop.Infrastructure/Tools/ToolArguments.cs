using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
internal static class ToolArguments
{

    internal static double ReadDouble(JsonElement arguments, string property)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(property, out var value))
        {
            throw new ArgumentException($"falta el parametro requerido '{property}'");
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            // Algunos modelos mandan los numeros como string; aceptarlo evita un turno perdido.
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => throw new ArgumentException($"'{property}' no es un numero: {value.GetRawText()}"),
        };
    }

    internal static string? ReadString(JsonElement arguments, string property)
        => arguments.ValueKind == JsonValueKind.Object &&
           arguments.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    internal static string ReadRequiredString(JsonElement arguments, string property)
        => ReadString(arguments, property) is { Length: > 0 } value
            ? value
            : throw new ArgumentException($"falta el parametro requerido '{property}'");

    internal static string ResolvePath(JsonElement arguments, string property)
    {
        var raw = ReadRequiredString(arguments, property);
        return Path.GetFullPath(raw, Environment.CurrentDirectory);
    }

    internal static int ReadInt(JsonElement arguments, string property, int fallback)
        => arguments.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;
}
