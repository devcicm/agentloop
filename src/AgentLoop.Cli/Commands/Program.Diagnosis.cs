using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    /// <summary>
    /// Test honesto con el modelo real: ejecuta el goal contra GLM 5.3
    /// (o el modelo que se pase con --model) y reporta honestamente el
    /// resultado. Sin ScriptedRunner, sin cÃ¡ntico hardcoded, sin
    /// ScriptedStep. Si el modelo cumple el protocolo de tarjetas,
    /// imprime CUMPLIDO; si no, imprime SIN CERRAR con el motivo.
    ///
    /// Uso:
    ///   dotnet run --project AgentLoop.csproj -- lobos
    ///   dotnet run --project AgentLoop.csproj -- lobos --model z-ai/glm-5.3-free --deadline 60
    /// </summary>
    private static async Task<int> LobosAsync(string[] args, CancellationToken cancellationToken)
    {
        // El objetivo del poema es el primer argumento posicional; los
        // flags (--model, --deadline, etc.) se pasan tal cual.
        var objetivo = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : "Escribe el cÃ¡ntico inicial del doblaje latino de Wolf's Rain en notes/wolf-rain-poem.md y verifÃ­calo contra una fuente real.";

        // Reusa el flujo del comando goal: parsea los mismos flags, monta
        // policy y modelo, y delega en GoalAsync para que la ejecucion sea
        // exactamente la misma que cualquier otro goal del CLI.
        var lobosArgs = new[] { "goal", objetivo }
            .Concat(args.Skip(1).Where(a => !string.Equals(a, objetivo, StringComparison.Ordinal)
                && !string.Equals(a, "lobos", StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        var model = ResolveModel(lobosArgs);
        var deadline = IntOption(lobosArgs, "--deadline", 60);
        var maxCycles = IntOption(lobosArgs, "--max-ciclos", 5);
        var thinking = ResolveThinkingPolicy(lobosArgs);
        var stream = ResolveStreaming(lobosArgs);

        SecretStore.TryGetLLMApiKey(out var lobosKey, out _);
        Console.WriteLine($"key       : {SecretStore.Mask(lobosKey ?? string.Empty)}");
        Console.WriteLine($"modelo    : {model}");
        Console.WriteLine($"deadline  : {deadline}s");
        Console.WriteLine($"max-ciclos: {maxCycles}");
        Console.WriteLine($"thinking  : {DescribeThinking(thinking, model)}");
        Console.WriteLine($"modo      : {(stream ? "stream" : "respuesta completa")}");
        Console.WriteLine();

        // Preflight: si el modelo no esta disponible, se advierte y NO se
        // ejecuta la prueba. Correr un goal de minutos contra un modelo que
        // devuelve 403/404 es gastar tiempo para nada. Se puede saltar con
        // --sin-check para diagnosticar el fallo real del gateway.
        var skipCheck = HasFlag(lobosArgs, "--sin-check") || HasFlag(lobosArgs, "--skip-model-check");
        if (!skipCheck)
        {
            Console.WriteLine($"Sondeando disponibilidad de {model}...");
            using var probeClient = BuildClient(out _);
            var (available, reason) = await ProbeModelAvailabilityAsync(probeClient, model, cancellationToken).ConfigureAwait(false);

            if (!available)
            {
                Console.WriteLine();
                Console.WriteLine("ADVERTENCIA: el modelo no esta disponible; NO se ejecuta la prueba de lobos.");
                Console.WriteLine($"  motivo : {reason}");
                Console.WriteLine($"  accion : revisa el catalogo con 'discover', recarga saldo, o pasa --model <otro>.");
                Console.WriteLine($"           para forzar la ejecucion de todos modos: --sin-check");
                return 2;
            }

            Console.WriteLine($"modelo disponible: {reason}");
            Console.WriteLine();
        }
        else
        {
            Console.WriteLine("(preflight de modelo omitido con --sin-check)");
            Console.WriteLine();
        }

        Console.WriteLine("Ejecutando goal con el modelo real. Sin mocks, sin atajos.");
        Console.WriteLine();

        var exit = await GoalAsync(lobosArgs, cancellationToken).ConfigureAwait(false);

        // Banner final honesto: nada de VEREDICTO_PASS prefabricado.
        Console.WriteLine();
        Console.WriteLine(exit == 0
            ? "FINAL: el goal se cumplio (ledger.GoalMet=true)."
            : "FINAL: el goal NO se cumplio. Ver la seccion 'SIN CERRAR' arriba para el motivo del reactor.");

        return exit;
    }


    /// <summary>
    /// Diagnostico post-mortem de un run a partir de su snapshot persistido.
    /// Solo lee disco: no toca la red ni ejecuta el modelo.
    ///
    /// Uso:
    ///   dotnet run --project AgentLoop.csproj -- diagnostico
    ///   dotnet run --project AgentLoop.csproj -- diagnostico last --journal-dir artifacts/journeys
    ///   dotnet run --project AgentLoop.csproj -- diagnostico &lt;runId|ruta&gt;
    ///
    /// Devuelve 0 si el informe se imprimio (aunque el run diagnosticado haya
    /// fallado: diagnosticar no es ejecutar) y 1 si el snapshot no se pudo leer.
    /// </summary>
    private static Task<int> DiagnosticoAsync(string[] args)
    {
        var journalDir = StringOption(args, "--journal-dir")
            ?? Path.Combine(SecretStore.ResolveRepositoryRoot(), "artifacts", "journeys");
        var selector = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : "last";

        string? path = null;
        if (selector is "last" or "ultimo" or "")
        {
            path = Directory.Exists(journalDir)
                ? Directory.EnumerateFiles(journalDir, "*.json", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(f => new FileInfo(f).LastWriteTimeUtc)
                    .FirstOrDefault()
                : null;
        }
        else if (Guid.TryParse(selector, out var id))
        {
            path = Path.Combine(journalDir, $"{id:N}.json");
        }
        else
        {
            var candidate = Path.IsPathRooted(selector) ? selector : Path.Combine(journalDir, selector);
            path = candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? candidate : candidate + ".json";
        }

        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine($"diagnostico: no se encontro snapshot '{selector}' en {journalDir}");
            return Task.FromResult(1);
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"diagnostico: no se pudo leer {path}: {ex.Message}");
            return Task.FromResult(1);
        }

        var d = Harness.Core.JourneyDiagnosis.Analyze(json, SecretStore.ResolveRepositoryRoot());
        PrintDiagnosis(d, path);
        return Task.FromResult(0);
    }


    private static void PrintDiagnosis(Harness.Core.JourneyDiagnosis d, string path)
    {
        Console.WriteLine("--- diagnostico ----------------------------------------");
        Console.WriteLine($"snapshot : {path}");
        Console.WriteLine($"run      : {d.RunId}");
        Console.WriteLine($"objetivo : {(d.Objective.Length == 0 ? "(vacio)" : d.Objective)}");
        Console.WriteLine($"estado   : {d.Status}");
        Console.WriteLine($"cierre   : {d.Classification}");
        Console.WriteLine($"motivo   : {(string.IsNullOrEmpty(d.FailureReason) ? "(ninguno)" : d.FailureReason)}");
        Console.WriteLine($"elapsed  : {d.ElapsedSeconds:0}s | pasos: {d.StepCount}");
        Console.WriteLine($"tarjetas : abrir={d.Abrir} avanzar={d.Avanzar} comprobar={d.Comprobar} cerrar={d.Cerrar} abortar={d.Abortar} rechazadas={d.Rejected}");
        Console.WriteLine($"narracion: {d.Narrations} sin herramientas (avisos con ejemplo: {d.Nudges}, seguidos de tarjeta: {d.NudgesFollowedByCard})");
        Console.WriteLine($"procesos : iniciados={d.ProcessStarts} terminados={d.ProcessEnds} codigo!=0: {d.NonZeroExits}");
        Console.WriteLine($"pausa max: {d.MaxStepGapSeconds:0}s ({(d.MaxStepGapWhere.Length == 0 ? "sin datos" : d.MaxStepGapWhere)})");
        Console.WriteLine($"tokens   : no persistidos en el snapshot (ver resumen del log en vivo)");
        Console.WriteLine($"archivos : {(d.ReferencedFiles.Count == 0 ? "(ninguno referenciado)" : string.Join(", ", d.ReferencedFiles.Select(f => d.ExistingFiles.Contains(f, StringComparer.OrdinalIgnoreCase) ? f + " [existe]" : f + " [ausente]")))}");
        Console.WriteLine($"evidencia: {(d.EvidenceAggregate.Length == 0 ? "(no registrada en el snapshot)" : $"{d.EvidenceVerified} archivo(s) verificados, agregado {d.EvidenceAggregate}")}");
        Console.WriteLine($"accion   : {d.RecommendedAction}");
    }


    private static int InspectOperation(string[] args)
    {
        if (args.Length < 2) return Fail("Uso: recuperacion <ruta.jsonl>");
        var inspection = Harness.Durable.OperationEventJournal.Inspect(Path.GetFullPath(args[1]));
        Console.WriteLine(JsonSerializer.Serialize(inspection, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Inspeccion de evidencia: no se repiten efectos automaticamente.");
        return inspection.UnresolvedActions.Count == 0 && inspection.UnconfirmedEvents == 0 ? 0 : 1;
    }

}
