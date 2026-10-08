
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
        TimeSpan? serverDelay = null;

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(serverDelay ?? BackoffFor(attempt), cancellationToken).ConfigureAwait(false);
            }

            if (RequestAttemptBudget.Current.Value is { } budget && !budget.Acquire())
                throw new FailureException(new("budget.http_exhausted", "Presupuesto compartido de solicitudes al proveedor agotado.", Guid.NewGuid().ToString("N")));
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

            if (response.RequestMessage?.RequestUri is { } finalUri
                && !_options.AllowedOrigins.Any(origin => Uri.TryCreate(origin,UriKind.Absolute,out var allowed) && allowed.GetLeftPart(UriPartial.Authority) == finalUri.GetLeftPart(UriPartial.Authority)))
            { response.Dispose(); throw new FailureException(new("provider.redirect_denied","Respuesta de origen no autorizado.",Guid.NewGuid().ToString("N"))); }
            var status = (int)response.StatusCode;
            if (TransientErrorClassifier.IsRetryableStatus(status) && attempt < _options.MaxRetries)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);
                serverDelay = retryAfter is { } delay ? TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds,0,_options.RetryAfterCapSeconds)) : null;
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
        return TimeSpan.FromSeconds(seconds * (0.5 + Random.Shared.NextDouble() * 0.5));
    }

}
