using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Security.Contracts;

public sealed record ToolFailureDto(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("effects_may_have_occurred")] bool EffectsMayHaveOccurred = false);
