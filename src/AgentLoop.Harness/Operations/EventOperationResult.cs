using AgentLoop.Harness.Functional;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
namespace AgentLoop.Goals;

public sealed record EventOperationResult(OperationStatus Status, string Reason, [property: System.Text.Json.Serialization.JsonIgnore] Exception? Error = null)
{
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N");
    public TimeSpan Elapsed { get; init; }
    public AgentLoop.Operations.BudgetSnapshot? Budget { get; init; }
    public int HttpAttempts { get; init; }
    public Failure? FailureInfo { get; init; }
    public Result<Unit, Failure> Outcome => IsSuccess ? Result<Unit, Failure>.Ok(Unit.Value)
        : Result<Unit, Failure>.Fail(FailureInfo ?? (Error is null
            ? new Failure("operation." + Status.ToString().ToLowerInvariant(), "La operacion no completo sus criterios; consulta tarjetas y evidencia.", CorrelationId)
            : Failures.FromException(Error, CorrelationId)));
    public bool IsSuccess => Status == OperationStatus.Succeeded;
}
