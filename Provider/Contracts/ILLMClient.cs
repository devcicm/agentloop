using System.Text.Json;

namespace AgentLoop.Provider;

/// <summary>
/// CAPA 1 - comunicacion directa con el proveedor. Habla HTTP y nada mas:
/// devuelve el JSON crudo para que la capa de parseo lo interprete.
/// </summary>
public interface ILLMClient
{
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default);

    Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default);

    IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default);
}
