
namespace AgentLoop.Harness.Core;


/// <summary>Resultado de un Run, como tipo suma con dos casos.</summary>
public abstract record RunResult
{
    public sealed record Success(string Outcome) : RunResult;

    public sealed record Failure(string Reason, Exception? Error = null) : RunResult;

    public bool IsSuccess => this is Success;
    public bool IsFailure => this is Failure;

    public override string ToString() => this switch
    {
        Success s => $"success: {s.Outcome}",
        Failure f => $"failure: {f.Reason}",
        _ => "unknown",
    };
}
