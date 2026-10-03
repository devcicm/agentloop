using System.Text;
using System.Text.Json;
using AgentLoop.Provider;

namespace AgentLoop.Harness.Context;

public interface ITokenBudgetEstimator
{
    string Version { get; }
    bool Exact { get; }
    int Estimate(string text);
}

/// <summary>Conservative UTF-8 byte upper bound; explicitly not a tokenizer count.</summary>
public sealed class Utf8TokenBudgetEstimator : ITokenBudgetEstimator
{
    public string Version => "utf8-byte-upper-bound-v1";
    public bool Exact => false;
    public int Estimate(string text) => Encoding.UTF8.GetByteCount(text);
}

public static class ProjectContextBudget
{
    public static ChatRequest Fit(ChatRequest request, int maxInput, ITokenBudgetEstimator? estimator = null)
    {
        estimator ??= new Utf8TokenBudgetEstimator();
        var messages = request.Messages.ToList();
        int Size() => checked(estimator.Estimate(JsonSerializer.Serialize(request with { Messages = messages })) + 256);
        // Remove complete exchanges only; preserve system and the latest work brief.
        while (Size() > maxInput)
        {
            var next = messages.Count < 2 ? -1 : messages.FindIndex(2, m => m.Role == ChatRoles.User);
            if (next < 0) throw new InvalidOperationException("Contrato, tarea y schemas no caben en maxInputTokens (estimador conservador UTF-8, no exacto).");
            messages.RemoveRange(1, next - 1);
        }
        return request with { Messages = messages.ToArray() };
    }
}
