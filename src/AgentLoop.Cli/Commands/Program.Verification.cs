using System.Diagnostics;
using System.Xml.Linq;
namespace AgentLoop;
internal static partial class Program
{
    private static async Task<int> VerificationCommandAsync(string[] args, CancellationToken token)
    {
        var root = SecretStore.ResolveRepositoryRoot();
        var configured = StringOption(args, "--verification-project");
        if (configured is null && !Directory.Exists(Path.Combine(root,"tests")))
            throw new InvalidOperationException("selftest requiere fuentes, SDK y el proyecto tests/. El ZIP del CLI no incluye el verificador; usa el checkout de desarrollo o --verification-project.");
        var projects = configured is null ? Directory.EnumerateFiles(Path.Combine(root,"tests"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => XDocument.Load(path).Descendants("AgentLoopVerification").Any(x => x.Value == "true")).ToArray()
            : [Path.GetFullPath(configured,root)];
        if (projects.Length != 1) throw new InvalidOperationException("Indica --verification-project; se necesita un ejecutable de verificacion instalado.");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, WorkingDirectory = root };
        var configuration = typeof(Program).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyConfigurationAttribute), false)
            .OfType<System.Reflection.AssemblyConfigurationAttribute>().Single().Configuration;
        foreach (var argument in new[] {"run", "--no-build", "--configuration", configuration, "--project", projects[0], "--"}.Concat(args)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("No se pudo iniciar la verificacion.");
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) { if (!process.HasExited) process.Kill(entireProcessTree:true); throw; }
        return process.ExitCode;
    }
}
