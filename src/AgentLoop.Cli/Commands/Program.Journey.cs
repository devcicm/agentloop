using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    /// <summary>
    /// Comando 'journal': corre el harness via <see cref="GoalRunner"/> y persiste el
    /// Journey a artifacts/journeys/&lt;runId&gt;.md. Pensado para pruebas end-to-end
    /// del harness: el RunContext, StepRecorder y Durable se ejercitan de verdad
    /// sin tocar el flujo principal de 'goal'.
    /// </summary>
    private static async Task<int> JournalAsync(string[] args, CancellationToken cancellationToken)
    {
        var objective = DataGuard.Input(PositionalOrDefault(args, "averigua que version de dotnet hay instalada y dejala escrita en una nota"));
        var deadline = IntOption(args, "--deadline", 120);
        var maxCycles = IntOption(args, "--max-ciclos", 30);
        var stream = ResolveStreaming(args);
        var policy = ResolveThinkingPolicy(args);
        var model = ResolveModel(args);

        var run = Harness.Core.Run.FromObjective(objective, deadline, model);

        using var client = BuildClient(out var source, policy);

        var journalDir = StringOption(args, "--journal-dir")
            ?? Path.Combine(SecretStore.ResolveRepositoryRoot(), "artifacts", "journeys");
        var store = new Harness.Durable.FileRunStore(journalDir);

        var options = new Harness.Runner.RunnerOptions
        {
            DeadlineSeconds = deadline,
            MaxIterations = maxCycles,
            MaxTokens = IntOption(args, "--max-tokens", 2048),
            Thinking = policy,
            AllowShell = !HasFlag(args, "--no-shell"),
            AllowedPaths = StringOption(args, "--scope-root") is { } scopeRoot ? [Path.GetFullPath(scopeRoot)] : [],
            PolicyMode = StringOption(args, "--politica") ?? "auto",
            WorkingDirectory = StringOption(args, "--cwd") ?? Environment.CurrentDirectory,
        };

        Console.WriteLine($"key      : {source}");
        Console.WriteLine($"run      : {run.Id:N}");
        Console.WriteLine($"modelo   : {run.Model}");
        Console.WriteLine($"objetivo : {objective}");
        Console.WriteLine($"journal  : {journalDir}");
        Console.WriteLine();

        var (result, journey) = await Harness.Composition.GoalRunFactory.RunWithRunnerAsync(
            run, options, client, store, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine($"--- journey ------------------------------------------");
        Console.WriteLine($"status   : {journey.FinalStatus}");
        Console.WriteLine($"outcome  : {journey.Outcome}");
        Console.WriteLine($"steps    : {journey.Steps.Count} (model={journey.ModelCalls}, tool={journey.ToolCalls}, wait={journey.Waits}, err={journey.Errors})");
        Console.WriteLine($"elapsed  : {journey.Elapsed.TotalSeconds:0}s");

        var markdownPath = Path.Combine(journalDir, $"{run.Id:N}.md");
        Console.WriteLine();
        Console.WriteLine($"markdown : {markdownPath}");

        return result.IsSuccess ? 0 : 1;
    }

}
