using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    private static Task<int> ExecuteAsync(string[] args)
        => GuardedAsync(cancellationToken => DispatchAsync(args, cancellationToken));


    /// <summary>
    /// Cancelacion y traduccion de errores a codigo de salida. Vive aparte porque el
    /// modo interactivo entra aqui una vez por comando y no puede morirse en el primero.
    /// </summary>
    private static async Task<int> GuardedAsync(Func<CancellationToken, Task<int>> action)
    {
        using var cancellation = new CancellationTokenSource();

        // El modo interactivo llama aqui una vez por comando: el handler se
        // desengancha al salir para no acumular suscripciones muertas.
        void OnCancel(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
            Console.WriteLine("\nCancelando...");
        }

        Console.CancelKeyPress += OnCancel;

        try
        {
            var result = await AgentLoop.Observability.ResultBoundary.CaptureAsync("cli.command", action,
                cancellation.Token, ObserveBoundary, code => code == 0);
            return result.Match(code => code, ReportFailure);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancel;
        }
    }


    private static async Task<int> DispatchAsync(string[] args, CancellationToken cancellationToken)
    {
        var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";

        if (HasFlag(args, "--project-contract"))
            return command switch
            {
                "project-admit" => ProjectAdmissionCommand(args),
                "project-check-backend" => await ProjectBackendCheckAsync(args, cancellationToken),
                "project-inspect" or "project-reconcile" => ProjectRecoveryCommand(args, command),
                "project-run" or "goal" or "objetivo" or "run" or "journal" or "diario" => await ProjectRunAsync(args, cancellationToken),
                _ => Fail("--project-contract requiere project-admit, project-run, project-inspect o project-reconcile."),
            };

        return command switch
        {
            "version" or "--version" => PrintVersion(),
            "setup" => SetupCredentials(args),
            "credenciales" => args.Length == 2 && args[1] == "verificar"
                ? await VerifyCredentialsAsync(cancellationToken) : Fail("Uso: credenciales verificar"),
            "project-admit" => ProjectAdmissionCommand(args),
            "project-run" => await ProjectRunAsync(args, cancellationToken),
            "project-inspect" or "project-reconcile" => ProjectRecoveryCommand(args, command),
            "selftest-projects" => await VerificationCommandAsync(args, cancellationToken),
            "selftest-events" => await VerificationCommandAsync(args, cancellationToken),
            "selftest-coordinator" => await VerificationCommandAsync(args, cancellationToken),
            "recuperacion" => InspectOperation(args),
            "chat" => await ChatAsync(args, cancellationToken),
            "discover" => await DiscoverAsync(args, cancellationToken),
            "run" => await GoalAsync(args, cancellationToken),
            "run-chat" => await RunAsync(args, cancellationToken),
            "goal" or "objetivo" => await GoalAsync(args, cancellationToken),
            "journal" or "diario" => await JournalAsync(args, cancellationToken),
            "lobos" => await LobosAsync(args, cancellationToken),
            "diagnostico" or "diagnosis" => await DiagnosticoAsync(args),
            "selftest" => await VerificationCommandAsync(args, cancellationToken),
            "help" or "--help" or "-h" or "ayuda" => PrintHelp(),
            "interactive" or "interactivo" or "repl" => await InteractiveAsync(args, cancellationToken),
            _ => Fail($"Comando desconocido: '{command}'"),
        };
    }

}
