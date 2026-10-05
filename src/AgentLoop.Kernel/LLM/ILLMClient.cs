using System.Text.Json;

namespace AgentLoop.Provider;


/// <summary>Port for model requests. Implementations own provider transport and dialect adaptation.</summary>
public interface ILLMClient
{
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
