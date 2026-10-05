using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
public static class CapabilityProbeTools
{

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
            var city = ToolArguments.ReadString(arguments, "city") ?? "unknown";
            return ValueTask.FromResult(JsonSerializer.Serialize(new { city, temperature_c = 21, condition = "sunny" }));
        });
}
