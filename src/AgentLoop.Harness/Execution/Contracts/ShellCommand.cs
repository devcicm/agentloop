
namespace AgentLoop.Terminals;


/// <summary>Un comando a ejecutar en un interprete concreto.</summary>
public sealed record ShellCommand(
    ShellKind Shell,
    string Command,
    string? WorkingDirectory = null,
    string? Label = null)
{
    public static ShellKind ParseShell(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => OperatingSystem.IsWindows() ? ShellKind.PowerShell : ShellKind.Bash,
        "bash" => ShellKind.Bash,
        "sh" => ShellKind.Sh,
        "powershell" or "ps" or "ps1" => ShellKind.PowerShell,
        "pwsh" => ShellKind.Pwsh,
        "cmd" or "bat" or "batch" => ShellKind.Cmd,
        var other => throw new InvalidOperationException(
            $"interprete desconocido: {other}. Usa bash, sh, powershell, pwsh o cmd."),
    };

    public override string ToString()
        => $"{Shell.ToString().ToLowerInvariant()}: {(Command.Length > 120 ? Command[..120] + "..." : Command)}";
}
