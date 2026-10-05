using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
public static class SystemTools
{

    public static DelegateTool GetTime { get; } = new(
        name: "get_time",
        description: "Devuelve la hora UTC actual del sistema en formato ISO-8601.",
        parametersJsonSchema: """
        {
          "type": "object",
          "properties": {
            "label": { "type": "string", "description": "Etiqueta opcional para identificar la consulta" }
          }
        }
        """,
        handler: (arguments, _) =>
        {
            var label = ToolArguments.ReadString(arguments, "label");
            return ValueTask.FromResult(JsonSerializer.Serialize(new
            {
                utc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                label,
            }));
        });

    public static DelegateTool GetSystemContext { get; } = new(
        "get_system_context", "Consulta el directorio actual, sistema operativo y carpetas conocidas reales. No expone credenciales ni variables de entorno.",
        """{"type":"object","properties":{}}""",
        (_, _) => ValueTask.FromResult(JsonSerializer.Serialize(new
        {
            working_directory = Environment.CurrentDirectory,
            operating_system = Environment.OSVersion.ToString(),
            executable_resolver = OperatingSystem.IsWindows() ? "where.exe" : "which",
            user_directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            desktop_directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            documents_directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            temporary_directory = Path.GetTempPath(),
        })));

    public static DelegateTool WhereCommand { get; } = new(
        "where_command", "Resuelve la ruta de un ejecutable usando where.exe en Windows o which en Unix.",
        """{"type":"object","properties":{"command":{"type":"string"}},"required":["command"]}""",
        async (arguments, cancellationToken) =>
        {
            var command = ToolArguments.ReadRequiredString(arguments, "command");
            if (command.StartsWith('-') || command.StartsWith('/') && OperatingSystem.IsWindows()
                || command.Contains('\r') || command.Contains('\n'))
                throw new ArgumentException("command debe ser un nombre o ruta de ejecutable, no opciones ni comandos de shell.");
            var resolver = OperatingSystem.IsWindows() ? Path.Combine(Environment.SystemDirectory, "where.exe") : "which";
            var info = new ProcessStartInfo { FileName = resolver, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            info.ArgumentList.Add(command);
            using var process = Process.Start(info) ?? throw new InvalidOperationException($"no se pudo iniciar {resolver}");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            string output;
            string error;
            try
            {
                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                output = await stdout.ConfigureAwait(false);
                error = await stderr.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                throw;
            }
            var paths = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(p => Path.GetFullPath(p.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return JsonSerializer.Serialize(new { command, resolver, exit_code = process.ExitCode, paths, error = error.Trim() });
        });
}
