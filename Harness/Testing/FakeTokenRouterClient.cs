using System.Text.Json;
using AgentLoop.Provider;

namespace AgentLoop.Harness.Testing;

/// <summary>
/// Cliente fake de TokenRouter que no hace red: devuelve una respuesta
/// JSON vacia (sin tool_calls, sin contenido) para que el self-test del
/// Composition Root pueda ejercitar el cableado sin depender del
/// proveedor.
///
/// Vive en <c>AgentLoop.Harness.Testing</c> para que su naturaleza de
/// fixture de test sea evidente desde el namespace. Si alguien desde
/// codigo de produccion lo importa, ya esta senializando que esta
/// usando un mock.
/// </summary>
public sealed class FakeTokenRouterClient : ITokenRouterClient
{
    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ModelInfo>>([]);

    public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var json = """{"id":"fake","choices":[{"index":0,"finish_reason":"stop","message":{"role":"assistant","content":""}}],"usage":{"prompt_tokens":0,"completion_tokens":0,"total_tokens":0}}""";
        return Task.FromResult(JsonDocument.Parse(json).RootElement.Clone());
    }

    public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        yield break;
    }
}
