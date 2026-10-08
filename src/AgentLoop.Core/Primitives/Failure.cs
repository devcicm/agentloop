namespace AgentLoop.Harness.Functional;

/// <summary>Error seguro para transporte, observabilidad y decisiones; nunca contiene payloads o secretos.</summary>
public sealed record Failure(string Code, string Message, string CorrelationId, bool Retryable = false, int? StatusCode = null);

public interface IFailureSource
{
    Failure ToFailure(string correlationId);
}

public sealed class FailureException(Failure failure) : Exception(failure.Message), IFailureSource
{
    public Failure Failure { get; } = failure;
    public Failure ToFailure(string correlationId) => Failure with { CorrelationId = correlationId };
}

public static class Failures
{
    public static Failure FromException(Exception error, string correlationId) => error switch
    {
        IFailureSource source => source.ToFailure(correlationId),
        OperationCanceledException => new("operation.cancelled", "Operacion cancelada o limite de tiempo agotado.", correlationId),
        UnauthorizedAccessException => new("storage.denied", "Acceso al recurso local denegado.", correlationId),
        IOException => new("storage.io", "Fallo de entrada o salida local.", correlationId),
        System.Net.Http.HttpRequestException => new("provider.transport", "No se pudo conectar con el proveedor.", correlationId, true),
        ArgumentException => new("input.invalid", "Configuracion o argumento invalido.", correlationId),
        InvalidOperationException => new("operation.invalid", "La operacion no puede continuar con el estado actual.", correlationId),
        _ => new("operation.unexpected", "Fallo inesperado; consulta el identificador de correlacion.", correlationId),
    };
}
