using System.Diagnostics;
using AgentLoop.Harness.Functional;

namespace AgentLoop.Observability;

/// <summary>Frontera de captura con trazas sin argumentos, resultados, excepciones ni datos sensibles.</summary>
public static class ResultBoundary
{
    public static readonly ActivitySource Activities = new("AgentLoop.Boundaries");

    public static async Task<Result<T, Failure>> CaptureAsync<T>(string operation, Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default, Action<BoundaryObservation>? observe = null, Func<T, bool>? isSuccessful = null)
    {
        using var activity = Activities.StartActivity(operation) ?? new Activity(operation).Start();
        var correlation = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N");
        var clock = Stopwatch.StartNew();
        Result<T, Failure> result;
        var success = false;
        try
        {
            var value = await action(cancellationToken).ConfigureAwait(false);
            success = isSuccessful?.Invoke(value) ?? true;
            result = Result<T, Failure>.Ok(value);
        }
        catch (Exception error) { result = Result<T, Failure>.Fail(Failures.FromException(error, correlation)); }
        activity?.SetTag("result.success", success);
        activity?.SetTag("error.code", result.Error?.Code ?? (success ? null : "operation.nonzero"));
        activity?.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        // Un observador no puede alterar el resultado de la operacion que ya se completo.
        try { observe?.Invoke(new(operation, correlation, success, result.Error?.Code ?? (success ? null : "operation.nonzero"), clock.Elapsed)); }
        catch { activity?.SetTag("observer.failed", true); }
        return result;
    }
}

public sealed record BoundaryObservation(string Operation, string CorrelationId, bool Success, string? ErrorCode, TimeSpan Elapsed);
