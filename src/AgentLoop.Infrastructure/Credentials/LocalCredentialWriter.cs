using AgentLoop.Harness.Functional;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace AgentLoop.Configuration;

/// <summary>Persistencia local de secretos; no pertenece al kernel ni se expone como herramienta.</summary>
public static class LocalCredentialWriter
{
    public static string Destination => Path.Combine(SecretStore.ResolveSecretsDirectory(), SecretStore.ApiKeyFileName);

    public static Result<Unit, Failure> SaveResult(string value, bool replace)
    {
        try { Save(value, replace); return Result<Unit, Failure>.Ok(Unit.Value); }
        catch (Exception error) { return Result<Unit, Failure>.Fail(Failures.FromException(error, Guid.NewGuid().ToString("N"))); }
    }

    public static void Save(string value, bool replace)
    {
        if (value.Length is < 16 or > 4096 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl))
            throw new InvalidOperationException("Credencial invalida: usa un unico valor sin espacios ni saltos de linea.");
        var path = Destination;
        var directory = Path.GetDirectoryName(path)!;
        for (var parent = new DirectoryInfo(directory); parent is not null; parent = parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("El almacen no puede atravesar enlaces o puntos de redireccion.");
        if (File.Exists(path) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("La credencial no puede ser un enlace.");
        if (File.Exists(path) && !replace) throw new InvalidOperationException("Ya existe una credencial; requiere confirmar reemplazo.");
        CheckGitIsolation(path);
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".credential-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (OperatingSystem.IsWindows())
                {
                    var identity = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No se pudo resolver el usuario local.");
                    var acl = new FileSecurity();
                    acl.SetAccessRuleProtection(true, false);
                    acl.SetOwner(identity);
                    acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
                    stream.SetAccessControl(acl);
                }
                else File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                var bytes = Encoding.UTF8.GetBytes(value);
                try { stream.Write(bytes); stream.Flush(true); }
                finally { Array.Clear(bytes); }
            }
            File.Move(temporary, path, replace);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void CheckGitIsolation(string path)
    {
        var root = Path.GetDirectoryName(path)!;
        var hasRepository = false;
        for (var parent = new DirectoryInfo(root); parent is not null; parent = parent.Parent)
            if (Directory.Exists(Path.Combine(parent.FullName, ".git")) || File.Exists(Path.Combine(parent.FullName, ".git"))) { hasRepository = true; root=parent.FullName; break; }
        if (!hasRepository) return; // Distribucion sin Git: sigue usando el almacen dedicado.
        int Git(params string[] arguments)
        {
            var info = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in arguments) info.ArgumentList.Add(argument);
            using var process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo comprobar la exclusion Git.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new InvalidOperationException("La comprobacion Git excedio su limite."); }
            Task.WhenAll(output, error).GetAwaiter().GetResult();
            return process.ExitCode;
        }
        if (Git("ls-files", "--error-unmatch", "--", path) != 1 || Git("check-ignore", "--quiet", "--", path) != 0)
            throw new InvalidOperationException("No se guardara: el destino esta versionado o no esta excluido de Git.");
    }
}
