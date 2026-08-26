using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentLoop.Parsing;

/// <summary>
/// Junta los chunks de un stream en una respuesta con la misma forma que la no-streaming.
///
/// La gracia es que no reimplementa el parseo: arma el JSON equivalente y se lo pasa a
/// <see cref="CompletionParser"/>. Asi el thinking, las tool calls y el consumo salen
/// tipados igual vengan de un stream o de una llamada normal, y el bloque de tool_calls
/// se puede reenviar al proveedor tal como espera en el turno siguiente.
/// </summary>
public sealed class StreamAccumulator
{
    private sealed class ToolCallBuffer
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public StringBuilder Arguments { get; } = new();
    }

    private readonly StringBuilder _content = new();
    private readonly StringBuilder _thinking = new();
    private readonly SortedDictionary<int, ToolCallBuffer> _toolCalls = [];

    private string _id = string.Empty;
    private string _model = string.Empty;
    private string _finishReason = string.Empty;
    private long _created;
    private JsonElement? _usage;

    /// <summary>Chunks recibidos, incluidos los que solo traen keep-alive o usage.</summary>
    public int Chunks { get; private set; }

    /// <summary>Chunks que traian algo: texto, razonamiento o tool calls.</summary>
    public int Deltas { get; private set; }

    public string Content => _content.ToString();

    /// <summary>Razonamiento acumulado en crudo; la traza tipada sale de <see cref="Build"/>.</summary>
    public string ThinkingText => _thinking.ToString();

    public bool HasThinking => _thinking.Length > 0;

    public bool HasToolCalls => _toolCalls.Count > 0;

    public string FinishReason => _finishReason;

    /// <summary>El proveedor mando el bloque de usage (requiere stream_options.include_usage).</summary>
    public bool HasUsage => _usage is not null;

    /// <summary>Acumula un chunk y devuelve el delta ya normalizado, para poder pintarlo en vivo.</summary>
    public StreamDelta Add(JsonElement chunk)
    {
        Chunks++;

        if (_id.Length == 0)
        {
            _id = ReadString(chunk, "id");
        }

        if (_model.Length == 0)
        {
            _model = ReadString(chunk, "model");
        }

        if (_created == 0 && chunk.TryGetProperty("created", out var created) &&
            created.ValueKind == JsonValueKind.Number)
        {
            _created = created.GetInt64();
        }

        // El chunk final suele venir sin choices y solo con el consumo.
        if (chunk.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            _usage = usage.Clone();
        }

        var delta = CompletionParser.ParseStreamDelta(chunk);

        _content.Append(delta.Content);
        _thinking.Append(delta.Thinking);

        foreach (var toolDelta in delta.ToolCallDeltas)
        {
            MergeToolCall(toolDelta);
        }

        if (delta.FinishReason.Length > 0)
        {
            _finishReason = delta.FinishReason;
        }

        if (!delta.IsEmpty)
        {
            Deltas++;
        }

        return delta;
    }

    /// <summary>La respuesta completa, ya tipada. Se puede llamar en cualquier momento.</summary>
    public ParsedCompletion Build() => CompletionParser.Parse(BuildRaw());

    /// <summary>El JSON equivalente al de una respuesta no-streaming.</summary>
    public JsonElement BuildRaw()
    {
        var message = new JsonObject { ["role"] = "assistant" };

        var content = _content.ToString();
        message["content"] = content.Length > 0 ? content : null;

        if (_thinking.Length > 0)
        {
            message["reasoning_content"] = _thinking.ToString();
        }

        if (_toolCalls.Count > 0)
        {
            var calls = new JsonArray();
            foreach (var (index, buffer) in _toolCalls)
            {
                calls.Add(new JsonObject
                {
                    // Sin id no hay a que atar el resultado de la tool: se inventa uno estable.
                    ["id"] = buffer.Id.Length > 0 ? buffer.Id : $"call_{index}",
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = buffer.Name,
                        ["arguments"] = buffer.Arguments.Length > 0 ? buffer.Arguments.ToString() : "{}",
                    },
                });
            }

            message["tool_calls"] = calls;
        }

        var root = new JsonObject
        {
            ["id"] = _id,
            ["object"] = "chat.completion",
            ["model"] = _model,
            ["created"] = _created,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                // Algunos backends cierran el stream sin finish_reason; se deduce del contenido.
                ["finish_reason"] = _finishReason.Length > 0
                    ? _finishReason
                    : _toolCalls.Count > 0 ? "tool_calls" : "stop",
                ["message"] = message,
            }),
        };

        if (_usage is not null)
        {
            root["usage"] = JsonNode.Parse(_usage.Value.GetRawText());
        }

        using var document = JsonDocument.Parse(root.ToJsonString());
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Los argumentos de una tool call llegan troceados y hay que concatenarlos en orden.
    /// El indice es la identidad: el id y el nombre solo vienen en el primer trozo.
    /// </summary>
    private void MergeToolCall(JsonElement toolDelta)
    {
        if (toolDelta.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var index = toolDelta.TryGetProperty("index", out var indexNode) && indexNode.ValueKind == JsonValueKind.Number
            ? indexNode.GetInt32()
            : _toolCalls.Count == 0 ? 0 : _toolCalls.Keys.Max();

        if (!_toolCalls.TryGetValue(index, out var buffer))
        {
            buffer = new ToolCallBuffer();
            _toolCalls[index] = buffer;
        }

        var id = ReadString(toolDelta, "id");
        if (id.Length > 0)
        {
            buffer.Id = id;
        }

        if (!toolDelta.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = ReadString(function, "name");
        if (name.Length > 0)
        {
            buffer.Name = name;
        }

        buffer.Arguments.Append(ReadString(function, "arguments"));
    }

    private static string ReadString(JsonElement element, string property)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(property, out var value) &&
           value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
