using System.Text.Json;
using AgentLoop.Observability;

namespace AgentLoop;

internal static partial class Program
{
    private static readonly object ObservationGate = new();

    private static int ReportFailure(Failure failure)
    {
        ConsoleUi.Error($"{failure.Code}: {failure.Message}");
        ConsoleUi.Field("Correlacion", failure.CorrelationId);
        ObserveBoundary(new("cli.failure", failure.CorrelationId, false, failure.Code, TimeSpan.Zero));
        return failure.Code == "operation.cancelled" ? 130 : 1;
    }

    private static void ObserveBoundary(BoundaryObservation observation)
    {
        try
        {
            lock (ObservationGate)
            {
                var directory = Path.Combine(SecretStore.ResolveRepositoryRoot(), ".agentloop");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "boundary-results.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 10 * 1024 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                // Solo metadatos definidos por el host; no parametros, valores, cuerpos ni mensajes de excepcion.
                File.AppendAllText(path, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, observation.Operation,
                    observation.CorrelationId, observation.Success, observation.ErrorCode, elapsedMs = observation.Elapsed.TotalMilliseconds }) + "\n");
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ConsoleUi.Error("observability.unavailable: no se pudo guardar el registro de resultados.");
        }
    }

    private static void ObserveOperationResult(EventOperationResult result)
    {
        ObserveBoundary(new("harness.operation", result.CorrelationId, result.IsSuccess, result.Outcome.Error?.Code, TimeSpan.Zero));
        if (!result.IsSuccess) ReportFailure(result.Outcome.Error!);
    }

    private static async Task<int> VerifyCredentialsAsync(CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        var result = await ResultBoundary.CaptureAsync("credentials.verify", async token =>
        {
            using var client = BuildClient(out _);
            await client.ListModelsAsync(token);
            return Unit.Value;
        }, deadline.Token, ObserveBoundary);
        if (!result.IsOk) return ReportFailure(result.Error!);
        ConsoleUi.Status("El endpoint de catalogo acepto la solicitud. No acredita acceso a cada modelo ni a herramientas.", true);
        return 0;
    }
}
