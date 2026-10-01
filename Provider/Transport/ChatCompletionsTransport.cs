namespace AgentLoop.Provider;

/// <summary>Shared cancellable HTTP retries; no model-specific payload decisions.</summary>
public sealed class ChatCompletionsTransport(HttpClient http, TransportOptions options)
{
    private readonly HttpClient _http = http;
    private readonly TransportOptions _options = options;
    public async Task<HttpResponseMessage> SendWithRetryAsync(
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

                throw new LLMException(0, $"Error de transporte: {ex.Message}", inner: ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastTransportError = ex;
                if (attempt < _options.MaxRetries)
                {
                    continue;
                }

                throw new LLMException(0, $"Timeout tras {_options.Timeout.TotalSeconds:0}s", inner: ex);
            }

            var status = (int)response.StatusCode;
            if (TransientErrors.IsRetryableStatus(status) && attempt < _options.MaxRetries)
            {
                response.Dispose();
                continue;
            }

            return response;
        }

        throw new LLMException(0, $"Agotados {_options.MaxRetries} reintentos", inner: lastTransportError);
    }

    private TimeSpan BackoffFor(int attempt)
    {
        var seconds = Math.Min(_options.BackoffBaseSeconds * Math.Pow(2, attempt - 1), _options.MaxBackoffSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

}
