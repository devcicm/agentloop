using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace AgentLoop.Terminals;


/// <summary>Resultado de una ejecucion con espera.</summary>
public sealed record ShellResult(
    string ProcessId,
    ShellCommand Command,
    int? ExitCode,
    bool TimedOut,
    bool Simulated,
    TimeSpan Duration,
    IReadOnlyList<OutputLine> Output,
    bool Truncated)
{
    /// <summary>Vista como tipo suma. Ok solo si termino a tiempo con codigo 0.</summary>
    public Harness.Functional.Result<ShellOutcome, ShellFailureReason> Result
        => TimedOut
            ? Harness.Functional.Result<ShellOutcome, ShellFailureReason>.Fail(ShellFailureReason.TimedOut)
            : ExitCode == 0
                ? Harness.Functional.Result<ShellOutcome, ShellFailureReason>.Ok(new ShellOutcome(ExitCode ?? 0, Output, Truncated, Duration))
                : Harness.Functional.Result<ShellOutcome, ShellFailureReason>.Fail(ExitCode.HasValue ? ShellFailureReason.NonZeroExit : ShellFailureReason.Unknown);
}
