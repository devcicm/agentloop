
namespace AgentLoop.Provider;


/// <summary>Model controls are explicit; a numeric thinking budget is not a GLM 5.3 limit.</summary>
public static class NvidiaRequestAdapter
{
    public static ChatRequest Adapt(ChatRequest request, bool stream)
    {
        var kwargs = request.ChatTemplateKwargs is null
            ? new Dictionary<string, object?>() : new Dictionary<string, object?>(request.ChatTemplateKwargs);
        if (NvidiaModelCatalog.IsGlm53(request.Model))
        {
            kwargs["clear_thinking"] = true;
            kwargs.Remove("enable_thinking");
            var effort = request.ReasoningEffort ?? "low";
            if (effort is not ("low" or "high" or "max")) throw new ArgumentException("GLM 5.3: reasoning_effort debe ser low, high o max.");
            return request with
            {
                Stream = stream ? true : null, StreamOptions = stream ? request.StreamOptions : null,
                Thinking = null, ReasoningEffort = effort, ChatTemplateKwargs = kwargs,
                Messages = request.Messages.Select(m => new ChatMessage
                {
                    Role = m.Role, Content = m.Content, Name = m.Name,
                    ToolCallId = m.ToolCallId, ToolCalls = m.ToolCalls,
                }).ToArray(),
            };
        }
        // Other model dialects retain their existing behavior; no GLM assumptions for Kimi.
        if (request.Thinking is not null)
        {
            kwargs["enable_thinking"] = request.Thinking.Mode == ThinkingMode.Enabled;
            kwargs["clear_thinking"] = false;
        }
        return request with
        {
            Stream = stream ? true : null, StreamOptions = stream ? request.StreamOptions : null,
            ChatTemplateKwargs = kwargs.Count == 0 ? null : kwargs,
        };
    }
}
