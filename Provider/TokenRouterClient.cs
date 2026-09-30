using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Provider;

/// <summary>
/// CAPA 1 - cliente asincrono OpenAI-compatible para NVIDIA NIM.
/// Puerto de tokenrouter-client/tokenrouter/client.py.
/// </summary>
public sealed class TokenRouterClient : ITokenRouterClient, IDisposable
{
    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TokenRouterOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    public TokenRouterClient(TokenRouterOptions options, HttpClient? httpClient = null)
    {
        _options = options;
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new TokenRouterException(401, "Falta la API key. Ver local-secrets/README.md");
        }

        _ownsHttpClient = httpClient is null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = options.Timeout;
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
        catch (TokenRouterException ex) when (request.Thinking is not null && RejectsThinking(ex))
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
        catch (TokenRouterException ex) when (request.Thinking is not null && RejectsThinking(ex))
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

        var response = await SendWithRetryAsync(
            () => BuildRequest(HttpMethod.Post, "/chat/completions", payload),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw BuildException((int)response.StatusCode, errorBody);
        }
    }

    /// <summary>
    /// Distingue "no conozco este campo" de un 400 de verdad. Solo se degrada cuando el
    /// error nombra el parametro: si no, reintentar sin thinking taparia el error real.
    /// </summary>
    private static bool RejectsThinking(TokenRouterException ex)
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
        using var reader = new StreamReader(stream, Encoding.UTF8);

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
                using var doc = JsonDocument.Parse(data);
                chunk = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                continue; // chunk truncado o malformado: se ignora, igual que el cliente Python
            }

            yield return chunk;
        }
    }

    private async Task<JsonElement> SendJsonAsync(HttpMethod method, string path, string? body, CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            () => BuildRequest(method, path, body),
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);

        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildException((int)response.StatusCode, content);
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            return doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new TokenRouterException(
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
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        Exception? lastTransportError = null;

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(BackoffFor(attempt), cancellationToken).ConfigureAwait(false);
            }

            HttpResponseMessage response;
            try
            {
                using var request = requestFactory();
                response = await _http.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                lastTransportError = ex;
                if (attempt < _options.MaxRetries)
                {
                    continue;
                }

                throw new TokenRouterException(0, $"Error de transporte: {ex.Message}", inner: ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastTransportError = ex;
                if (attempt < _options.MaxRetries)
                {
                    continue;
                }

                throw new TokenRouterException(0, $"Timeout tras {_options.Timeout.TotalSeconds:0}s", inner: ex);
            }

            var status = (int)response.StatusCode;
            if ((status >= 500 || status == 429) && attempt < _options.MaxRetries)
            {
                response.Dispose();
                continue;
            }

            return response;
        }

        throw new TokenRouterException(0, $"Agotados {_options.MaxRetries} reintentos", inner: lastTransportError);
    }

    private TimeSpan BackoffFor(int attempt)
    {
        var seconds = Math.Min(Math.Pow(_options.BackoffBaseSeconds, attempt - 1), _options.MaxBackoffSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    private HttpRequestMessage BuildRequest(HttpMethod method, string path, string? body)
    {
        var request = new HttpRequestMessage(method, $"{_options.BaseUrl.TrimEnd('/')}{path}");
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        return request;
    }

    /// <summary>stream_options solo tiene sentido con stream=true; fuera de ahi se omite.</summary>
    private static ChatRequest WithStream(ChatRequest request, bool stream)
        => request with
        {
            Stream = stream ? true : null,
            StreamOptions = stream ? request.StreamOptions : null,
            ChatTemplateKwargs = request.Thinking is not null
                ? new Dictionary<string, object?>
                {
                    ["enable_thinking"] = request.Thinking.Mode == ThinkingMode.Enabled,
                    ["clear_thinking"] = false,
                }
                : null,
        };

    private static string ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static TokenRouterException BuildException(int status, string body)
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
