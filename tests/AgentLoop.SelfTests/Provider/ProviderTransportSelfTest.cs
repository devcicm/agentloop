using System.Net;

namespace AgentLoop.Provider;

/// <summary>Contratos offline de fronteras HTTP: origen, reintentos y cuota compartida.</summary>
public static class ProviderTransportSelfTest
{
    public static async Task<bool> RunAsync(CancellationToken token)
    {
        var checks = new List<(string Name, bool Ok)>();
        void Check(string name, bool ok)
        {
            checks.Add((name, ok));
            Console.WriteLine((ok ? "OK " : "FAIL ") + name);
        }

        try
        {
            ProviderEndpointPolicy.Validate("http://example.test/v1", ["https://example.test"]);
            Check("endpoint HTTP se rechaza", false);
        }
        catch (FailureException)
        {
            Check("endpoint HTTP se rechaza", true);
        }

        var calls = 0;
        using (var client = new HttpClient(new SequenceHandler(() =>
        {
            calls++;
            return calls == 1 ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK);
        })))
        {
            var options = Options(1);
            var transport = new ChatCompletionsTransport(client, options);
            var parent = new RequestAttemptBudget(2);
            var previous = RequestAttemptBudget.Current.Value;
            RequestAttemptBudget.Current.Value = parent.CreateScope(() => true);
            try
            {
                using var response = await transport.SendWithRetryAsync(Request, HttpCompletionOption.ResponseHeadersRead, token);
                Check("reintento transitorio usa respuesta posterior", response.StatusCode == HttpStatusCode.OK && calls == 2 && parent.Attempts == 2);
            }
            finally { RequestAttemptBudget.Current.Value = previous; }
        }

        using (var client = new HttpClient(new SequenceHandler(() => new(HttpStatusCode.ServiceUnavailable))))
        {
            var transport = new ChatCompletionsTransport(client, Options(1));
            var previous = RequestAttemptBudget.Current.Value;
            RequestAttemptBudget.Current.Value = new RequestAttemptBudget(1).CreateScope(() => true);
            try
            {
                await transport.SendWithRetryAsync(Request, HttpCompletionOption.ResponseHeadersRead, token);
                Check("cuota compartida bloquea segundo intento", false);
            }
            catch (FailureException error)
            {
                Check("cuota compartida bloquea segundo intento", error.Failure.Code == "budget.http_exhausted");
            }
            finally { RequestAttemptBudget.Current.Value = previous; }
        }

        return checks.All(x => x.Ok);
    }

    private static TransportOptions Options(int retries) => new()
    {
        ApiKey = "fixture-only",
        BaseUrl = "https://example.test/v1",
        AllowedOrigins = ["https://example.test"],
        MaxRetries = retries,
        BackoffBaseSeconds = 0,
        MaxBackoffSeconds = 0,
    };

    private static HttpRequestMessage Request() => new(HttpMethod.Get, "https://example.test/v1/models");

    private sealed class SequenceHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var result = response();
            result.RequestMessage = request;
            return Task.FromResult(result);
        }
    }
}
