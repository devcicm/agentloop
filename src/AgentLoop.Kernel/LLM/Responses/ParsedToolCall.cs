using System.Text.Json;

namespace AgentLoop.Parsing;


/// <summary>
/// Una tool call ya normalizada. <see cref="Arguments"/> viene deserializado desde el string
/// JSON que manda el proveedor; si ese string estaba roto, <see cref="ArgumentsParseFailed"/>
/// queda en true y <see cref="RawArguments"/> conserva el texto original.
/// </summary>
public sealed record ParsedToolCall(
    string Id,
    string Name,
    JsonElement Arguments,
    string RawArguments,
    bool ArgumentsParseFailed)
{
    public override string ToString() => $"{Name}({RawArguments})";
}
