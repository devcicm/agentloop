using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Security.Contracts;

public sealed record DataFindingDto(string Code, string Source);
public sealed record ToolDataMetadataDto(int Version, string Source, string Trust, string Sha256,
    bool Truncated, IReadOnlyList<DataFindingDto> Findings);

/// <summary>Reserved metadata belongs to the harness; extension fields remain external data.</summary>
public sealed class ToolResultDto
{
    [JsonPropertyName("_harness")]
    public required ToolDataMetadataDto Metadata { get; init; }
    [JsonExtensionData]
    public Dictionary<string, JsonElement> Data { get; init; } = new();
}

public sealed record PromptDataDto(int Version, string Source, string Trust, string Text);
public sealed record ToolFailureDto(
    [property: JsonPropertyName("error")] string Error,
    [property: JsonPropertyName("effects_may_have_occurred")] bool EffectsMayHaveOccurred = false);
public sealed record ModelRequestDto(
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("max_tokens")] int? MaxTokens,
    [property: JsonPropertyName("reasoning_effort")] string? ReasoningEffort,
    [property: JsonPropertyName("recovery")] bool Recovery,
    [property: JsonPropertyName("tool_choice")] string? ToolChoice,
    [property: JsonPropertyName("history_characters")] int HistoryCharacters,
    [property: JsonPropertyName("messages")] int Messages,
    [property: JsonPropertyName("numeric_thinking_budget_is_provider_limit")] bool NumericThinkingBudgetIsProviderLimit = false);
