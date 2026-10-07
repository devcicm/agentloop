using System.Reflection;
namespace AgentLoop;

internal static partial class Program
{
    private static int PrintVersion()
    {
        var assembly = typeof(Program).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString() ?? "desconocida";
        Console.WriteLine("AgentLoop " + version);
        return 0;
    }
}
