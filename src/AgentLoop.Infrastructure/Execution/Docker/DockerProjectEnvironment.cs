using System.Diagnostics;
using System.Text;
using System.Security.Cryptography;
namespace AgentLoop.Harness.Execution;


public sealed class DockerProjectEnvironment(string root, Guid runId, DockerExecutionDefinition definition,
    IReadOnlyList<ProjectCommandDefinition> commands, ProjectRuntimePolicy policy, Action<ProjectProcessNotice>? observer = null) : IProjectExecutionEnvironment
{
    private string _docker => definition.Executable;
    private string _oracle => definition.AssetRoot;
    private string? _image;
    private int _running;
    public int Running => Volatile.Read(ref _running);
    public string Identity => "docker:" + (_image ?? "unprepared");

    public async Task CheckPreviousRunAsync(Guid runId, CancellationToken token)
    {
        var result = await InvokeAsync(["ps", "--filter", "label=agentloop.run=" + runId.ToString("N"), "--format", "{{.Names}}"], TimeSpan.FromSeconds(definition.ControlTimeoutSeconds), token);
        if (result.ExitCode != 0 || !string.IsNullOrWhiteSpace(result.Output))
            throw new InvalidOperationException("Recuperacion bloqueada: contenedores previos vivos o estado no comprobable: " + result.Output);
    }

    public async Task PreflightAsync(CancellationToken token)
    {
        AgentLoop.Goals.Projects.ProjectContractAdmission.CheckExecutionRoot(root);
        if (!File.Exists(_docker)) throw new InvalidOperationException("Backend aislado ausente: Docker CLI no encontrado en su ruta dedicada.");
        var info = await InvokeAsync(["info", "--format", "{{.OSType}}"], TimeSpan.FromSeconds(definition.ControlTimeoutSeconds), token);
        if (info.ExitCode != 0 || info.Output.Trim() != "linux")
            throw new InvalidOperationException("Backend aislado no disponible: iniciar el motor Linux de Docker. " + DataGuard.Display(info.Output));
        var image = await InvokeAsync(["image", "inspect", "--format", "{{.Id}}", definition.Image], TimeSpan.FromSeconds(definition.ControlTimeoutSeconds), token);
        if (image.ExitCode != 0 || !image.Output.Trim().StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidOperationException("Falta la imagen local " + definition.Image + "; preparar el perfil externo.");
        _image = image.Output.Trim(); // Pin this execution to the resolved local immutable image ID.
        if (!Directory.Exists(_oracle)) throw new InvalidOperationException("Faltan recursos externos del perfil.");
        if (root.Contains(',') || _oracle.Contains(',')) throw new InvalidOperationException("El backend bind-mount no admite comas en estas rutas.");
        VerifyResources();
    }

    private void VerifyResources()
    {
        foreach (var resource in definition.ResourceHashes)
        {
            var path = Path.GetFullPath(resource.Key, _oracle);
            var relative = Path.GetRelativePath(_oracle, path);
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("Recurso fuera del perfil.");
            AgentLoop.Goals.Projects.ProjectContractAdmission.CheckResource(path);
            using var stream = File.OpenRead(path);
            if (stream.Length > policy.MaxArtifactBytes || !Convert.ToHexString(SHA256.HashData(stream)).Equals(resource.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Recurso del perfil modificado: " + resource.Key);
        }
    }

    public async Task<ProjectCommandResult> RunAsync(string profile, TimeSpan timeout, CancellationToken token)
    {
        if (_image is null) throw new InvalidOperationException("Ejecutar preflight antes del comando.");
        VerifyResources();
        var command = commands.SingleOrDefault(c => c.Id == profile) ?? throw new InvalidOperationException("Comando de perfil no autorizado.");
        timeout = timeout < TimeSpan.FromSeconds(command.TimeoutSeconds) ? timeout : TimeSpan.FromSeconds(command.TimeoutSeconds);
        var name = "agentloop-" + runId.ToString("N") + "-" + Guid.NewGuid().ToString("N");
        var args = new List<string> { "run", "--rm", "--name", name, "--label", "agentloop.run=" + runId.ToString("N"),
            "--network", "none", "--read-only", "--cap-drop", "ALL", "--security-opt", "no-new-privileges=true",
            "--user", definition.User, "--memory", definition.Memory, "--memory-swap", definition.Memory,
            "--cpus", definition.Cpus, "--pids-limit", definition.Pids.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--tmpfs", definition.TemporaryMount, "--workdir", definition.WorkingDirectory,
            "--mount", "type=bind,source=" + Path.GetFullPath(root) + ",target=" + definition.ProjectMount + ",readonly",
            "--mount", "type=bind,source=" + _oracle + ",target=" + definition.AssetMount + ",readonly" };
        foreach (var entry in definition.Environment) { args.Add("--env"); args.Add(entry.Key + "=" + entry.Value); }
        args.Add(_image);
        args.AddRange(definition.EntryPoint);
        args.AddRange(command.Arguments);
        Interlocked.Increment(ref _running);
        ProjectCommandResult? result = null;
        try { observer?.Invoke(new(profile, name, true, null, null)); result = await InvokeAsync(args, timeout, token); }
        finally
        {
            // Cleanup uses its own bounded budget even after cancellation, never a model-provided name.
            var absent = false;
            try
            {
                var removed = await InvokeAsync(["rm", "--force", name], TimeSpan.FromSeconds(definition.CleanupTimeoutSeconds), CancellationToken.None);
                var inspected = await InvokeAsync(["inspect", name], TimeSpan.FromSeconds(definition.CleanupTimeoutSeconds), CancellationToken.None);
                absent = removed.ExitCode == 0 || inspected.ExitCode == 1 && inspected.Output.Contains("No such", StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
                observer?.Invoke(new(profile, name, false, result?.ExitCode, absent));
            }
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
        try
        {
            if (!process.Start()) throw new IOException("No se pudo iniciar el ejecutable del backend.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IOException("No se pudo iniciar el ejecutable configurado del backend: " + DataGuard.Display(ex.Message), ex);
        }
        async Task<string> Capture(StreamReader reader)
        {
            var output = new StringBuilder();
            var buffer = new char[2048];
            int count;
            while ((count = await reader.ReadAsync(buffer)) > 0)
                if (output.Length < policy.MaxCommandOutputChars) output.Append(buffer, 0, Math.Min(count, policy.MaxCommandOutputChars - output.Length));
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
