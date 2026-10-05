using System.Text.Json;

namespace AgentLoop.Parsing;


/// <summary>
/// Consumo de tokens de una llamada; se acumula a lo largo del loop.
/// <see cref="ThinkingTokens"/> es la parte de completion que se fue en razonar: sale de
/// usage.completion_tokens_details.reasoning_tokens y ya viene incluida en CompletionTokens.
/// </summary>
public readonly record struct TokenUsage(
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    int ThinkingTokens = 0)
{
    public static TokenUsage Empty => default;

    public TokenUsage Add(TokenUsage other) => new(
        PromptTokens + other.PromptTokens,
        CompletionTokens + other.CompletionTokens,
        TotalTokens + other.TotalTokens,
        ThinkingTokens + other.ThinkingTokens);

    public override string ToString()
    {
        var thinking = ThinkingTokens > 0 ? $" (thinking={ThinkingTokens})" : string.Empty;
        return $"prompt={PromptTokens} completion={CompletionTokens}{thinking} total={TotalTokens}";
    }
}
