using System.Text;
using System.Text.Json;
namespace AgentLoop.Harness.Context;


public static class ProjectContextBudget
{
    public static ChatRequest Fit(ChatRequest request, int maxInput, ITokenBudgetEstimator? estimator = null, int margin = 0)
    {
        estimator ??= new Utf8TokenBudgetEstimator();
        var messages = request.Messages.ToList();
        int Size() => checked(estimator.Estimate(JsonSerializer.Serialize(request with { Messages = messages })) + margin);
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
