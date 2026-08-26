namespace AgentLoop.Provider;

/// <summary>Configuracion del cliente HTTP contra el gateway.</summary>
public sealed class TokenRouterOptions
{
    public const string DefaultBaseUrl = "https://api.tokenrouter.com/v1";

    /// <summary>Unico modelo confirmado como accesible con cuota $0.00 (ver README de tokenrouter-client).</summary>
    public const string FallbackModelId = "qwen/qwen3.8-max-free";

    public required string ApiKey { get; init; }

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>El gateway devuelve 503 intermitentes bajo carga; sin reintentos el harness es inusable.</summary>
    public int MaxRetries { get; init; } = 6;

    public double BackoffBaseSeconds { get; init; } = 2.0;

    public double MaxBackoffSeconds { get; init; } = 10.0;

    public string UserAgent { get; init; } = "agentloop-dotnet/0.1";
}
