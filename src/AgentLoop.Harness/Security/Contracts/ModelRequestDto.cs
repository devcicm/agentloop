using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Security.Contracts;

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
