using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace AgentLoop.Provider;


/// <summary>
/// CAPA 1 - cliente asincrono OpenAI-compatible para NVIDIA NIM.
/// Adaptacion y parseo del protocolo NVIDIA NIM.
/// </summary>
public sealed class NvidiaClient : ILLMClient, IDisposable
{
    private static readonly JsonSerializerOptions PayloadOptions = ChatPayloadSerializer.CreateOptions();

    private readonly NvidiaOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly ChatCompletionsTransport _transport;

    public NvidiaClient(NvidiaOptions options, HttpClient? httpClient = null)
    {
        if (options.MaxRetries < 0 || options.Timeout <= TimeSpan.Zero || options.BackoffBaseSeconds < 0 || options.MaxBackoffSeconds < 0 || options.RetryAfterCapSeconds < 0) throw new ArgumentException("Presupuestos de transporte invalidos.");
        ProviderEndpointPolicy.Validate(options.BaseUrl, options.AllowedOrigins);
        _options = options;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new LLMException(401, "Falta la API key. Ver local-secrets/README.md");
        }

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        _http.Timeout = options.Timeout;
        _transport = new(_http, options);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
    }

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var root = await SendJsonAsync(HttpMethod.Get, "/models", body: null, cancellationToken).ConfigureAwait(false);

        var models = new List<ModelInfo>();
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return models;
        }

        foreach (var item in data.EnumerateArray())
        {
            var endpoints = new List<string>();
            if (item.TryGetProperty("supported_endpoint_types", out var eps) && eps.ValueKind == JsonValueKind.Array)
            {
                endpoints.AddRange(eps.EnumerateArray()
                    .Where(e => e.ValueKind == JsonValueKind.String)
                    .Select(e => e.GetString()!));
            }

            models.Add(new ModelInfo(
                Id: ReadString(item, "id"),
                OwnedBy: ReadString(item, "owned_by"),
                Created: item.TryGetProperty("created", out var created) && created.ValueKind == JsonValueKind.Number
                    ? created.GetInt64()
                    : 0,
                SupportedEndpointTypes: endpoints,
                Tags: ReadString(item, "tags")));
        }

        return models;
    }

    /// <summary>
    /// Politica de thinking compartida con quien arma las peticiones. El cliente solo la
    /// usa para apuntar los modelos que rechazan el parametro; decidir cuando activarlo
    /// no es cosa de la capa de transporte.
    /// </summary>
    public ThinkingPolicy? ThinkingPolicy { get; init; }

    /// <summary>Aviso de que un modelo rechazo el thinking y se reintento sin el.</summary>
    public Action<string>? OnThinkingRejected { get; init; }

    public async Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var payload = JsonSerializer.Serialize(WithStream(request, stream: false), PayloadOptions);

        try
        {
            return await SendJsonAsync(HttpMethod.Post, "/chat/completions", payload, cancellationToken).ConfigureAwait(false);
        }
        catch (LLMException ex) when (!NvidiaModelCatalog.IsGlm53(request.Model) && request.Thinking is not null && RejectsThinking(ex))
        {
            // El backend no entiende el parametro. Perder el turno entero por un campo
            // opcional seria absurdo: se reintenta una vez sin el y se apunta el modelo
            // para que las siguientes peticiones salgan ya limpias.
            var first = ThinkingPolicy?.MarkUnsupported(request.Model) ?? true;
            if (first)
            {
                OnThinkingRejected?.Invoke($"{request.Model} rechaza thinking ({ex.ProviderMessage}); se reintenta sin el.");
            }

            var withoutThinking = JsonSerializer.Serialize(
                WithStream(request with { Thinking = null }, stream: false), PayloadOptions);

            return await SendJsonAsync(HttpMethod.Post, "/chat/completions", withoutThinking, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Abre el stream, degradando el thinking igual que en la llamada normal.</summary>
    private async Task<HttpResponseMessage> OpenStreamAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await SendStreamRequestAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (LLMException ex) when (!NvidiaModelCatalog.IsGlm53(request.Model) && request.Thinking is not null && RejectsThinking(ex))
        {
            var first = ThinkingPolicy?.MarkUnsupported(request.Model) ?? true;
            if (first)
            {
                OnThinkingRejected?.Invoke($"{request.Model} rechaza thinking ({ex.ProviderMessage}); se reintenta sin el.");
            }

            return await SendStreamRequestAsync(request with { Thinking = null }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<HttpResponseMessage> SendStreamRequestAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(WithStream(request, stream: true), PayloadOptions);

        var response = await _transport.SendWithRetryAsync(
            () => BuildRequest(HttpMethod.Post, "/chat/completions", payload),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var errorBody = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            throw BuildException((int)response.StatusCode, errorBody);
        }
    }

    /// <summary>
    /// Distingue "no conozco este campo" de un 400 de verdad. Solo se degrada cuando el
    /// error nombra el parametro: si no, reintentar sin thinking taparia el error real.
    /// </summary>
    private static bool RejectsThinking(LLMException ex)
    {
        if (ex.StatusCode is not (400 or 404 or 422))
        {
            return false;
        }

        return Mentions(ex.ProviderMessage) || Mentions(ex.RawPayload);

        static bool Mentions(string? text) =>
            text is not null &&
            (text.Contains("thinking", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("budget_tokens", StringComparison.OrdinalIgnoreCase) ||
             text.Contains("reasoning", StringComparison.OrdinalIgnoreCase));
    }

    public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // La apertura va aparte porque un iterador no puede llevar try/catch alrededor de
        // un yield, y el reintento sin thinking tiene que poder ocurrir antes del primer chunk.
        using var response = await OpenStreamAsync(request, cancellationToken).ConfigureAwait(false);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var bounded = new BoundedReadStream(stream, DataLimits.ProviderBytes);
        using var reader = new StreamReader(bounded, Encoding.UTF8);

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            // Lineas vacias separan eventos SSE; las que abren con ':' son comentarios keep-alive.
            if (line.Length == 0 || line[0] == ':')
            {
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();
            if (data == "[DONE]")
            {
                yield break;
            }

            JsonElement chunk;
            try
            {
                chunk = StrictJson.Parse(data, DataLimits.ProviderBytes);
            }
            catch (JsonException)
            {
                throw new LLMException(422, "SSE invalido o ambiguo; respuesta rechazada.");
            }

            yield return chunk;
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var bodyStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var boundedBody = new BoundedReadStream(bodyStream, DataLimits.ProviderBytes);
        using var bodyReader = new StreamReader(boundedBody, Encoding.UTF8);
        return await bodyReader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonElement> SendJsonAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        using var response = await _transport.SendWithRetryAsync(
            () => BuildRequest(method, path, body),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        var content = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException((int)response.StatusCode, content);
        }

        try
        {
            return StrictJson.Parse(DataGuard.Redact(content), DataLimits.ProviderBytes);
        }
        catch (JsonException ex)
        {
            throw new LLMException(
                (int)response.StatusCode,
                $"La respuesta no es JSON valido: {ex.Message}",
                Truncate(content),
                ex);
        }
    }

    /// <summary>
    /// Reintenta ante 5xx, 429 y fallos de transporte con backoff exponencial acotado.
    /// Cada intento construye un HttpRequestMessage nuevo porque no son reutilizables.
    /// </summary>
    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? body)
    {
        var request = new HttpRequestMessage(method, $"{_options.BaseUrl.TrimEnd('/')}{path}");
        if (body is not null)
        {
            if (body.Length > DataLimits.ProviderBytes) throw new InvalidOperationException("Request body exceeds its size budget.");
            request.Content = new StringContent(DataGuard.Redact(body), Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>stream_options solo tiene sentido con stream=true; fuera de ahi se omite.</summary>
    private static ChatRequest WithStream(ChatRequest request, bool stream)
        => NvidiaRequestAdapter.Adapt(request, stream);

    private static string ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static LLMException BuildException(int status, string body)
        => new(status, ExtractErrorMessage(body), Truncate(body));

    /// <summary>Saca error.message del cuerpo; si no hay forma, devuelve el cuerpo recortado.</summary>
    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(sin cuerpo de respuesta)";
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
            {
                if (error.ValueKind == JsonValueKind.String)
                {
                    return error.GetString() ?? body;
                }

                if (error.ValueKind == JsonValueKind.Object)
                {
                    if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                    {
                        var code = ReadString(error, "code");
                        return code.Length > 0 ? $"{message.GetString()} ({code})" : message.GetString()!;
                    }

                    return error.GetRawText();
                }
            }

            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("message", out var topLevel) &&
                topLevel.ValueKind == JsonValueKind.String)
            {
                return topLevel.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // cuerpo no-JSON (por ejemplo el HTML de un proxy): cae al recorte de abajo
        }

        return Truncate(body, 300);
    }

    private static string Truncate(string value, int max = 2000)
        => value.Length <= max ? value : value[..max] + "...";

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
