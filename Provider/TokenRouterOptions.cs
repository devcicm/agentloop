namespace AgentLoop.Provider;

/// <summary>Configuracion del cliente HTTP contra el gateway.</summary>
public sealed class TokenRouterOptions
{
    public const string DefaultBaseUrl = "https://api.tokenrouter.com/v1";

    /// <summary>
    /// Unico modelo agentico accesible con la cuenta y cuota actuales
    /// (saldo $0.00 devuelve 403 en todos los demas). Verificado contra
    /// artifacts/agentic-models.json (refresh tras cada discover).
    /// </summary>
    public const string FallbackModelId = "z-ai/glm-5.3-free";

    public required string ApiKey { get; init; }

    public string BaseUrl { get; init; } = DefaultBaseUrl;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>El gateway devuelve 503 intermitentes bajo carga; sin reintentos el harness es inusable.</summary>
    public int MaxRetries { get; init; } = 6;

    public double BackoffBaseSeconds { get; init; } = 2.0;

    public double MaxBackoffSeconds { get; init; } = 10.0;

    public string UserAgent { get; init; } = "agentloop-dotnet/0.1";
}
