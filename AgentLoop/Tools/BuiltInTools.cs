using System.Globalization;
using System.Text.Json;

namespace AgentLoop.Tools;

/// <summary>Herramientas deterministas para ejercitar el loop sin depender de servicios externos.</summary>
public static class BuiltInTools
{
    public static DelegateTool AddNumbers { get; } = new(
        name: "add_numbers",
        description: "Suma dos numeros y devuelve el resultado exacto.",
        parametersJsonSchema: """
        {
          "type": "object",
          "properties": {
            "a": { "type": "number", "description": "Primer sumando" },
            "b": { "type": "number", "description": "Segundo sumando" }
          },
          "required": ["a", "b"]
        }
        """,
        handler: (arguments, _) =>
        {
            var a = ReadDouble(arguments, "a");
            var b = ReadDouble(arguments, "b");
            var sum = a + b;
            return ValueTask.FromResult(JsonSerializer.Serialize(new { a, b, sum }));
        });

    public static DelegateTool GetTime { get; } = new(
        name: "get_time",
        description: "Devuelve la hora UTC actual del sistema en formato ISO-8601.",
        parametersJsonSchema: """
        {
          "type": "object",
          "properties": {
            "label": { "type": "string", "description": "Etiqueta opcional para identificar la consulta" }
          }
        }
        """,
        handler: (arguments, _) =>
        {
            var label = ReadString(arguments, "label");
            return ValueTask.FromResult(JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                label,
            }));
        });

    /// <summary>Tool de sondeo: su unico proposito es ver si el modelo emite una tool call.</summary>
    public static DelegateTool GetWeather { get; } = new(
        name: "get_weather",
        description: "Get the current weather of a city.",
        parametersJsonSchema: """
        {
          "type": "object",
          "properties": {
            "city": { "type": "string", "description": "City name" }
          },
          "required": ["city"]
        }
        """,
        handler: (arguments, _) =>
        {
            var city = ReadString(arguments, "city") ?? "unknown";
            return ValueTask.FromResult(JsonSerializer.Serialize(new { city, temperature_c = 21, condition = "sunny" }));
        });

    /// <summary>Set por defecto que usa el subcomando "run".</summary>
    public static ToolRegistry DefaultRegistry() => new([AddNumbers, GetTime]);

    private static double ReadDouble(JsonElement arguments, string property)
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

    private static string? ReadString(JsonElement arguments, string property)
        => arguments.ValueKind == JsonValueKind.Object &&
           arguments.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
