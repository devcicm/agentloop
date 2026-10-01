using System.Net;
using System.Text;
using System.Text.Json;

namespace AgentLoop.Provider;

/// <summary>Offline payload tests through the real HTTP client and SSE path.</summary>
public static class ProviderContractSelfTest
{
    public static async Task RunAsync(Action<string, bool> check, CancellationToken token)
    {
        var requests = new List<JsonElement>();
        using var handler = new CaptureHandler(requests);
        using var http = new HttpClient(handler);
        using var client = new NvidiaClient(new NvidiaOptions { ApiKey = "fixture-only", MaxRetries = 0 }, http);
        var source = new ChatRequest
        {
            Model = NvidiaModelCatalog.Glm53, Messages = [ChatMessage.FromAssistant("respuesta", reasoning: "historia privada")],
            Thinking = ThinkingOptions.Enabled(256), MaxTokens = 2048, StreamOptions = StreamOptions.WithUsage,
        };
        await client.CreateChatCompletionAsync(source, token);
        await foreach (var chunk in client.StreamChatCompletionAsync(source with { Thinking = ThinkingOptions.Off }, token)) { }
        check("GLM HTTP y SSE usan low sin prometer budget numerico", requests.Count == 2 && requests.All(p =>
            p.GetProperty("reasoning_effort").GetString() == "low" && !p.TryGetProperty("thinking", out _)
            && p.GetProperty("chat_template_kwargs").GetProperty("clear_thinking").GetBoolean()));
        check("GLM omite reasoning previo sin mutar mensaje original", source.Messages[0].ReasoningContent == "historia privada"
            && requests.All(p => !p.GetProperty("messages")[0].TryGetProperty("reasoning_content", out _)));
        check("stream_options solo viaja en SSE", !requests[0].TryGetProperty("stream_options", out _)
            && requests[1].GetProperty("stream").GetBoolean() && requests[1].GetProperty("stream_options").GetProperty("include_usage").GetBoolean());
        var invalid = false;
        try { NvidiaRequestAdapter.Adapt(source with { ReasoningEffort = "medium" }, false); }
        catch (ArgumentException) { invalid = true; }
        check("GLM rechaza effort fuera de su dialecto", invalid);
        var kimi = NvidiaRequestAdapter.Adapt(source with { Model = NvidiaModelCatalog.KimiK3 }, false);
        check("Kimi no recibe controles de GLM por catalogo", kimi.ReasoningEffort is null && kimi.Thinking is not null);
    }

    private sealed class CaptureHandler(List<JsonElement> requests) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            requests.Add(doc.RootElement.Clone());
            var stream = doc.RootElement.TryGetProperty("stream", out var value) && value.GetBoolean();
            var body = stream ? "data: {\"choices\":[{\"delta\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"
                : "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}";
            return new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, stream ? "text/event-stream" : "application/json") };
        }
    }
}
