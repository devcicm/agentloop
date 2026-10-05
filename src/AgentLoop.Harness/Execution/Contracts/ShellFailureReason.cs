using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace AgentLoop.Terminals;


/// <summary>Motivo de fallo de una ejecucion de shell.</summary>
public enum ShellFailureReason
{
    TimedOut,
    NonZeroExit,
    Unknown,
}
