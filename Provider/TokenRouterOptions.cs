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

    /// <summary>
    /// Reintentos solo para 5xx y 429 transitorios. El backoff exponencial
    /// puede acumular segundos facilmente: 3 reintentos con base 1 y tope 5
    /// dan 1+2+4 = 7s de espera + los timeouts de cada intento. Con 6
    /// reintentos y base 2 (el valor anterior) se llegaba a 34s de espera
    /// solo en backoff, y con timeouts largos eso era el doble. 3 reintentos
    /// cubre el caso habitual del gateway sin que un solo timeout se
    /// convierta en minutos de espera.
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    public double BackoffBaseSeconds { get; init; } = 1.0;

    public double MaxBackoffSeconds { get; init; } = 5.0;

    public string UserAgent { get; init; } = "agentloop-dotnet/0.1";
}
