using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
namespace AgentLoop.Provider;
/// <summary>Wire serialization belongs to the provider adapter, outside neutral kernel contracts.</summary>
public static class ChatPayloadSerializer
{
    public static JsonSerializerOptions CreateOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new ThinkingPayloadConverter() }
    };
    public static string Serialize(ChatRequest request) => JsonSerializer.Serialize(request, CreateOptions());
    private sealed class ThinkingPayloadConverter : JsonConverter<ThinkingOptions>
    {
        public override ThinkingOptions Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            => throw new NotSupportedException("Los controles del proveedor solo se serializan.");
        public override void Write(Utf8JsonWriter writer, ThinkingOptions value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("type", value.Mode == ThinkingMode.Enabled ? "enabled" : "disabled");
            if (value.BudgetTokens is {} budget) writer.WriteNumber("budget_tokens", budget);
            writer.WriteEndObject();
        }
    }
}
