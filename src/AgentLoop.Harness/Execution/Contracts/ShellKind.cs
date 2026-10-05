using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AgentLoop.Terminals;


/// <summary>Interprete con el que se ejecuta el comando.</summary>
public enum ShellKind
{
    Bash,
    Sh,
    PowerShell,
    Pwsh,
    Cmd,
}
