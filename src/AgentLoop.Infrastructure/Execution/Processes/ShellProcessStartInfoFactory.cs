using System.Diagnostics;
namespace AgentLoop.Terminals;
public static class ShellProcessStartInfoFactory
{
    /// <summary>
    /// Los argumentos van por ArgumentList y no concatenados: asi el comando viaja como un
    /// unico argumento y no hay que pelearse con el escapado de comillas de cada interprete.
    /// </summary>
    public static ProcessStartInfo ToStartInfo(this ShellCommand command, string defaultWorkingDirectory)
    {
        var info = new ProcessStartInfo
        {
            WorkingDirectory = command.WorkingDirectory ?? defaultWorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        switch (command.Shell)
        {
            case ShellKind.Bash:
                info.FileName = "bash";
                info.ArgumentList.Add("-lc");
                info.ArgumentList.Add(command.Command);
                break;

            case ShellKind.Sh:
                info.FileName = "sh";
                info.ArgumentList.Add("-c");
                info.ArgumentList.Add(command.Command);
                break;

            case ShellKind.PowerShell:
                // Windows PowerShell donde existe; fuera de Windows solo hay pwsh.
                info.FileName = OperatingSystem.IsWindows() ? "powershell.exe" : "pwsh";
                AddPowerShellArguments(info, command);
                break;

            case ShellKind.Pwsh:
                info.FileName = "pwsh";
                AddPowerShellArguments(info, command);
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

                info.ArgumentList.Add(command.Command);
                break;

            default:
                throw new InvalidOperationException($"interprete no soportado: {command.Shell}");
        }

        return info;
    }

    private static void AddPowerShellArguments(ProcessStartInfo info, ShellCommand command)
    {
        // -NonInteractive es lo que evita que un prompt deje el proceso colgado para siempre,
        // que en un agente sin teclado delante es un cuelgue silencioso.
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-ExecutionPolicy");
        info.ArgumentList.Add("Bypass");
        info.ArgumentList.Add("-EncodedCommand");
        info.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(command.Command)));
    }

}
