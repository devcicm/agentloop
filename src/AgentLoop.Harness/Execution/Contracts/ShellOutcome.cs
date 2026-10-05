using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace AgentLoop.Terminals;


/// <summary>Outcome de un comando que salio bien: su codigo de salida y la salida capturada.</summary>
public sealed record ShellOutcome(int ExitCode, IReadOnlyList<OutputLine> Output, bool Truncated, TimeSpan Duration);
