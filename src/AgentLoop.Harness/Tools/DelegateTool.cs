using System.Text.Json;
namespace AgentLoop.Tools;


/// <summary>Adaptador para declarar una tool desde un delegado y un schema en texto.</summary>
public sealed class DelegateTool : ITool
{
    private readonly Func<JsonElement, CancellationToken, ValueTask<string>> _handler;
    private readonly JsonElement _parameters;

    public DelegateTool(
        string name,
        string description,
        string parametersJsonSchema,
        Func<JsonElement, CancellationToken, ValueTask<string>> handler)
    {
        Name = name;
        Description = description;
        _handler = handler;

        var schema = StrictJson.Parse(parametersJsonSchema);
        var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(schema.GetRawText())!;
        fields.TryAdd("additionalProperties", JsonSerializer.SerializeToElement(false));
        _parameters = JsonSerializer.SerializeToElement(fields);
    }

    public string Name { get; }

    public string Description { get; }

    public JsonElement Parameters => _parameters;

    public ValueTask<string> InvokeAsync(JsonElement arguments, CancellationToken cancellationToken)
        => _handler(arguments, cancellationToken);

    public ToolDefinition ToDefinition() => new()
    {
        Function = new ToolFunction
        {
            Name = Name,
            Description = Description,
            Parameters = Parameters,
        },
    };
}
