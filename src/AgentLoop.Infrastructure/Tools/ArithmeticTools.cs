using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
public static class ArithmeticTools
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
            var a = ToolArguments.ReadDouble(arguments, "a");
            var b = ToolArguments.ReadDouble(arguments, "b");
            var sum = a + b;
            return ValueTask.FromResult(JsonSerializer.Serialize(new { a, b, sum }));
        });
}
