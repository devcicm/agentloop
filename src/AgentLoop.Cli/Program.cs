using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    private static async Task<int> Main(string[] args)
    {
        ConsoleUi.Configure(args);
        if (ShouldRunInteractive(args))
        {
            return await GuardedAsync(token => InteractiveAsync(args, token));
        }

        var exitCode = await ExecuteAsync(args);
        PauseIfRequested(exitCode);
        return exitCode;
    }

}
