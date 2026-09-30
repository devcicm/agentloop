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

    /// <summary>
    /// Los argumentos van por ArgumentList y no concatenados: asi el comando viaja como un
    /// unico argumento y no hay que pelearse con el escapado de comillas de cada interprete.
    /// </summary>
    public ProcessStartInfo ToStartInfo(string defaultWorkingDirectory)
    {
        var info = new ProcessStartInfo
        {
            WorkingDirectory = WorkingDirectory ?? defaultWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        switch (Shell)
        {
            case ShellKind.Bash:
                info.FileName = "bash";
                info.ArgumentList.Add("-lc");
                info.ArgumentList.Add(Command);
                break;

            case ShellKind.Sh:
                info.FileName = "sh";
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add(Command);
                break;

            case ShellKind.PowerShell:
                // Windows PowerShell donde existe; fuera de Windows solo hay pwsh.
                info.FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
                AddPowerShellArguments(info);
                break;

            case ShellKind.Pwsh:
                info.FileName = "pwsh";
                AddPowerShellArguments(info);
                break;

            case ShellKind.Cmd:
                info.FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "sh";
                if (OperatingSystem.IsWindows())
                {
                    info.ArgumentList.Add("/d");
                    info.ArgumentList.Add("/c");
                }
                else
                {
                    info.ArgumentList.Add("-c");
                }

                info.ArgumentList.Add(Command);
                break;

            default:
                throw new InvalidOperationException($"interprete no soportado: {Shell}");
        }

        return info;
    }

    private void AddPowerShellArguments(ProcessStartInfo info)
    {
        // -NonInteractive es lo que evita que un prompt deje el proceso colgado para siempre,
        // que en un agente sin teclado delante es un cuelgue silencioso.
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-Command");
        info.ArgumentList.Add(Command);
    }

    public override string ToString()
        => $"{Shell.ToString().ToLowerInvariant()}: {(Command.Length > 120 ? Command[..120] + "..." : Command)}";
}

/// <summary>Como se tratan las ejecuciones antes de lanzarlas.</summary>
public enum PolicyMode
{
    /// <summary>Se ejecuta sin preguntar, salvo lo que caiga en la lista negra.</summary>
    Auto,

    /// <summary>Cada comando pasa por una confirmacion antes de arrancar.</summary>
    Confirmar,

    /// <summary>No se ejecuta nada: se responde que habria pasado. Util para revisar un plan.</summary>
    Simular,
}

/// <summary>Veredicto de la politica sobre un comando concreto.</summary>
public sealed record PolicyDecision(bool Allowed, string Reason, bool Simulated = false);

/// <summary>
/// El cinturon de seguridad de la ejecucion: directorio de trabajo, tope de duracion, tope
/// de procesos vivos y una lista negra de comandos irreversibles.
///
/// No es un sandbox y no pretende serlo: un agente con shell puede hacer cualquier cosa que
/// pueda hacer el usuario que lo lanza. Lo que hace la lista negra es evitar el accidente
/// tonto (un borrado de raiz, un formateo, un apagado), no contener a nadie decidido.
/// </summary>
public sealed class ExecutionPolicy
{
    private static readonly string[] DefaultDenied =
    [
        @"rm\s+(-\w+\s+)*(/|~|\$HOME)(\s|$)",
        @"\bmkfs(\.\w+)?\b",
        @"\bformat\s+[a-z]:",
        @"\bdd\b[^\n]*\bof=/dev/",
        @"\b(shutdown|reboot|halt|poweroff)\b",
        @"Clear-Disk|Format-Volume|Initialize-Disk",
        @"Remove-Item[^\n]*-Recurse[^\n]*(\\|/)(\s|$)",
        @"\bdel\s+/[sq]\b[^\n]*[a-z]:\\(\s|$)",
        @":\(\)\s*\{[^\n]*\|[^\n]*&\s*\}",
        @"\bgit\s+push[^\n]*--force",
    ];

    private readonly Regex[] _denied;

    public ExecutionPolicy(IEnumerable<string>? deniedPatterns = null)
    {
        DeniedPatterns = (deniedPatterns ?? DefaultDenied).ToArray();
        _denied = DeniedPatterns
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
    }

    public static ExecutionPolicy Default { get; } = new();

    public PolicyMode Mode { get; init; } = PolicyMode.Auto;

    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>Interprete que se usa cuando el agente no pide uno concreto.</summary>
    public ShellKind DefaultShell { get; init; } = ShellCommand.ParseShell(null);

    /// <summary>Tope por comando cuando quien llama no pide otro.</summary>
    public TimeSpan DefaultTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Tope duro: ni el agente puede pedir mas que esto.</summary>
    public TimeSpan MaxTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Procesos vivos a la vez. Evita que el agente llene la maquina de shells.</summary>
    public int MaxConcurrent { get; init; } = 4;

    public IReadOnlyList<string> DeniedPatterns { get; }

    /// <summary>Se consulta en modo Confirmar. Devolver false rechaza el comando.</summary>
    public Func<ShellCommand, bool>? Confirm { get; init; }
    public Func<ShellCommand, CancellationToken, ValueTask<bool>>? AsyncConfirm { get; set; }

    public async ValueTask<PolicyDecision> EvaluateAsync(ShellCommand command, CancellationToken token)
    {
        if (Mode != PolicyMode.Confirmar || AsyncConfirm is null) return Evaluate(command);
        var offender = _denied.FirstOrDefault(r => r.IsMatch(command.Command));
        if (offender is not null) return new(false, "comando vetado por politica");
        return await AsyncConfirm(command, token).ConfigureAwait(false) ? new(true, "confirmado") : new(false, "rechazado por operador");
    }

    public PolicyDecision Evaluate(ShellCommand command)
    {
        var offender = _denied.FirstOrDefault(r => r.IsMatch(command.Command));
        if (offender is not null)
        {
            return new PolicyDecision(false, $"bloqueado por la politica (patron {offender}); reformula sin eso.");
        }

        return Mode switch
        {
            PolicyMode.Simular => new PolicyDecision(true, "modo simulacion: no se ejecuta nada", Simulated: true),
            PolicyMode.Confirmar when Confirm is null => new PolicyDecision(false, "modo confirmar sin nadie que confirme"),
            PolicyMode.Confirmar => Confirm(command)
                ? new PolicyDecision(true, "confirmado")
                : new PolicyDecision(false, "rechazado en la confirmacion"),
            _ => new PolicyDecision(true, "permitido"),
        };
    }

    public TimeSpan Clamp(TimeSpan requested)
    {
        if (requested <= TimeSpan.Zero)
        {
            return DefaultTimeout;
        }

        return requested > MaxTimeout ? MaxTimeout : requested;
    }

    public override string ToString()
        => $"{Mode.ToString().ToLowerInvariant()} | {DefaultShell.ToString().ToLowerInvariant()} | cwd={WorkingDirectory} | timeout={DefaultTimeout.TotalSeconds:0}s (max {MaxTimeout.TotalMinutes:0}m) | {MaxConcurrent} procesos | {DeniedPatterns.Count} patrones vetados";
}
