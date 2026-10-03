using System.Diagnostics;
using System.Text;
using AgentLoop.Events;
using AgentLoop.Security;

namespace AgentLoop.Harness.Execution;

public sealed record ProjectCommandResult(int ExitCode, string Output, bool CleanupConfirmed);
public interface IProjectExecutionEnvironment
{
    int Running { get; }
    string Identity => "simulated-backend";
    Task CheckPreviousRunAsync(Guid runId, CancellationToken token) => Task.CompletedTask;
    Task PreflightAsync(CancellationToken token);
    Task<ProjectCommandResult> RunAsync(string profile, TimeSpan timeout, CancellationToken token);
}

/// <summary>Host-selected profiles only. No shell, no host env forwarded, read-only project mount, no external network.</summary>
public sealed class DockerProjectEnvironment(string root, EventBus bus) : IProjectExecutionEnvironment
{
    public const string ImageTag = "agentloop-project-node:1";
    private readonly string _docker = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Docker", "Docker", "resources", "bin", "docker.exe")
        : "/usr/bin/docker";
    private readonly string _oracle = Path.Combine(AppContext.BaseDirectory, "ProjectAssets");
    private string? _image;
    private int _running;
    public int Running => Volatile.Read(ref _running);
    public string Identity => "docker:" + (_image ?? "unprepared");

    public async Task CheckPreviousRunAsync(Guid runId, CancellationToken token)
    {
        var result = await InvokeAsync(["ps", "--filter", "label=agentloop.run=" + runId.ToString("N"), "--format", "{{.Names}}"], TimeSpan.FromSeconds(10), token);
        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.Output))
            throw new InvalidOperationException("Recuperacion bloqueada: contenedores previos vivos o estado no comprobable: " + result.Output);
    }

    public async Task PreflightAsync(CancellationToken token)
    {
        AgentLoop.Goals.Projects.ProjectContractAdmission.CheckExecutionRoot(root);
        if (!File.Exists(_docker)) throw new InvalidOperationException("Backend aislado ausente: Docker CLI no encontrado en su ruta dedicada.");
        var info = await InvokeAsync(["info", "--format", "{{.OSType}}"], TimeSpan.FromSeconds(10), token);
        if (info.ExitCode != 0 || info.Output.Trim() != "linux")
            throw new InvalidOperationException("Backend aislado no disponible: iniciar el motor Linux de Docker. " + DataGuard.Display(info.Output));
        var image = await InvokeAsync(["image", "inspect", "--format", "{{.Id}}", ImageTag], TimeSpan.FromSeconds(10), token);
        if (image.ExitCode != 0 || !image.Output.Trim().StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidOperationException("Falta la imagen local " + ImageTag + "; construir ProjectAssets/Dockerfile segun la guia.");
        _image = image.Output.Trim(); // Pin this execution to the resolved local immutable image ID.
        if (!File.Exists(Path.Combine(_oracle, "run.mjs"))) throw new InvalidOperationException("Faltan oraculos del host en ProjectAssets.");
        if (root.Contains(',') || _oracle.Contains(',')) throw new InvalidOperationException("El backend bind-mount no admite comas en estas rutas.");
    }

    public async Task<ProjectCommandResult> RunAsync(string profile, TimeSpan timeout, CancellationToken token)
    {
        if (_image is null) throw new InvalidOperationException("Ejecutar preflight antes del comando.");
        if (profile is not ("install" or "build" or "health" or "pos" or "security")) throw new InvalidOperationException("Perfil de comando no autorizado.");
        var name = "agentloop-" + bus.RunId.ToString("N") + "-" + Guid.NewGuid().ToString("N");
        var args = new List<string> { "run", "--rm", "--name", name, "--label", "agentloop.run=" + bus.RunId.ToString("N"),
            "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges=true",
            "--user", "1000:1000", "--memory", "768m", "--memory-swap", "768m", "--cpus", "1", "--pids-limit", "128",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=256m,mode=1777", "--workdir", "/tmp",
            "--mount", "type=bind,source=" + Path.GetFullPath(root) + ",target=/project,readonly",
            "--mount", "type=bind,source=" + _oracle + ",target=/oracle,readonly",
            "--env", "HOME=/tmp", "--env", "PLAYWRIGHT_BROWSERS_PATH=/opt/browsers", _image, "node", "/oracle/run.mjs", profile };
        Interlocked.Increment(ref _running);
        bus.Publish(AgentEventKind.ProcesoIniciado, "project-container", profile, new { container = name });
        ProjectCommandResult? result = null;
        try { result = await InvokeAsync(args, timeout, token); }
        finally
        {
            // Cleanup uses its own bounded budget even after cancellation, never a model-provided name.
            var removed = await InvokeAsync(["rm", "--force", name], TimeSpan.FromSeconds(10), CancellationToken.None);
            var inspected = await InvokeAsync(["inspect", name], TimeSpan.FromSeconds(5), CancellationToken.None);
            var absent = removed.ExitCode == 0 || inspected.ExitCode == 1 && inspected.Output.Contains("No such", StringComparison.OrdinalIgnoreCase);
            Interlocked.Decrement(ref _running);
            bus.Publish(AgentEventKind.ProcesoFin, "project-container", profile, new { container = name, codigo = result?.ExitCode, cleanupConfirmed = absent });
            if (!absent) throw new InvalidOperationException("Efecto incierto: no se pudo confirmar la eliminacion del contenedor " + name);
        }
        return result! with { CleanupConfirmed = true };
    }

    private async Task<ProjectCommandResult> InvokeAsync(IReadOnlyList<string> args, TimeSpan timeout, CancellationToken token)
    {
        var start = new ProcessStartInfo(_docker) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        var systemRoot = Environment.GetEnvironmentVariable("SystemRoot");
        start.Environment.Clear();
        if (systemRoot is not null) start.Environment["SystemRoot"] = systemRoot;
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new IOException("No se pudo iniciar Docker.");
        async Task<string> Capture(StreamReader reader)
        {
            var output = new StringBuilder();
            var buffer = new char[2048];
            int count;
            while ((count = await reader.ReadAsync(buffer)) > 0)
                if (output.Length < 64000) output.Append(buffer, 0, Math.Min(count, 64000 - output.Length));
            return DataGuard.Display(output.ToString());
        }
        var stdout = Capture(process.StandardOutput);
        var stderr = Capture(process.StandardError);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try { await process.WaitForExitAsync(deadline.Token); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(stdout, stderr);
            throw;
        }
        return new(process.ExitCode, (await stdout) + (await stderr), false);
    }
}
