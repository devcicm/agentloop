namespace AgentLoop.Provider;

/// <summary>Cualquier respuesta no-2xx del gateway o fallo de transporte.</summary>
public sealed class LLMException : Exception
{
    public LLMException(int statusCode, string message, string? rawPayload = null, Exception? inner = null)
        : base($"[{statusCode}] {message}", inner)
    {
        StatusCode = statusCode;
        ProviderMessage = message;
        RawPayload = rawPayload;
    }

    /// <summary>Codigo HTTP, o 0 si el fallo fue de transporte.</summary>
    public int StatusCode { get; }

    public string ProviderMessage { get; }

    public string? RawPayload { get; }

    /// <summary>La key no tiene saldo/permiso para este modelo. No es fatal durante el sondeo.</summary>
    public bool IsQuotaOrAccess =>
        StatusCode is 401 or 402 or 403 ||
        ProviderMessage.Contains("insufficient_user_quota", StringComparison.OrdinalIgnoreCase) ||
        ProviderMessage.Contains("quota", StringComparison.OrdinalIgnoreCase);

    /// <summary>Fallo transitorio del backend upstream: reintentable.</summary>
    public bool IsTransient => StatusCode == 0 || StatusCode >= 500 || StatusCode == 429;
}
