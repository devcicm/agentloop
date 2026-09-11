using AgentLoop.Cards;
using AgentLoop.Harness.Core;
using AgentLoop.Harness.Durable;
using AgentLoop.Harness.Observability;
using AgentLoop.Harness.Steps;

namespace AgentLoop.Harness.Runner;

/// <summary>
/// Runner que ejecuta una secuencia fija de tarjetas sin pasar por el LLM.
/// Pensado para verificar el flujo end-to-end del reactor, ledger, bus,
/// supervisor y journal sin depender de un modelo real.
///
/// La secuencia por defecto es: abrir -> N avanzar -> comprobar -> cerrar.
/// Asi se cubre el happy path del protocolo (transaccion abierta,
/// pasos, evidencia, cierre con GoalMet=true) y el ciclo completo del
/// reactor produce un Journey con Steps reales.
///
/// Caso de uso principal: cuando el modelo disponible (ej. GLM-5.3-free
/// en sesiones con saldo limitado) no cumple el protocolo de tarjetas,
/// ScriptedRunner sirve como test de regresion y demo del flujo exitoso.
/// Tambien es util para verificar cambios en el reactor sin gastar
/// creditos del proveedor.
/// </summary>
public sealed class ScriptedRunner : IRunner
{
    /// <summary>Secuencia por defecto: 1 abrir + 3 avanzar + 1 comprobar + 1 cerrar.</summary>
    public static IReadOnlyList<ScriptedStep> DefaultSequence { get; } = BuildDefault();

    private static IReadOnlyList<ScriptedStep> BuildDefault() => new ScriptedStep[]
    {
        new("abrir", new Dictionary<string, string>
        {
            ["objetivo"] = "probar el scripted runner",
            ["plan"] = "ejecutar la secuencia canonica de tarjetas",
        }),
        new("avanzar", new Dictionary<string, string> { ["paso"] = "primer paso" }),
        new("avanzar", new Dictionary<string, string> { ["paso"] = "segundo paso" }),
        new("avanzar", new Dictionary<string, string> { ["paso"] = "tercer paso" }),
        new("comprobar", new Dictionary<string, string>
        {
            ["comprobacion"] = "revisar que la transaccion sigue abierta",
            ["resultado"] = "3 pasos registrados",
        }),
        new("cerrar", new Dictionary<string, string>
        {
            ["resultado"] = "objetivo cumplido",
            ["evidencia"] = "secuencia completa ejecutada",
        }),
    };

    /// <summary>Paso de la secuencia: nombre de tarjeta + campos.</summary>
    public sealed record ScriptedStep(string Name, IReadOnlyDictionary<string, string> Fields);

    private readonly IReadOnlyList<ScriptedStep> _sequence;
    private readonly IRunStore? _store;

    public ScriptedRunner(IReadOnlyList<ScriptedStep>? sequence = null, IRunStore? store = null)
    {
        _sequence = sequence ?? DefaultSequence;
        _store = store;
    }

    public async Task<(RunResult Result, Journey Journey)> RunAsync(
        Core.Run run,
        RunnerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(options);

        var ledger = new CardLedger(CardDeck.Transaccional);
        var stepRecorder = new StepRecorder(run.Id, Array.Empty<IRunObserver>());
        var metrics = new MetricsCollector();
        var startedAt = DateTimeOffset.Now;

        // Cada paso genera un BusEventStep para que el Journey muestre la
        // secuencia completa, no solo el TransitionStep final.
        long seq = 0;
        foreach (var step in _sequence)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var play = ledger.Play(step.Name, step.Fields);
            stepRecorder.Record(new BusEventStep(
                Interlocked.Increment(ref seq),
                DateTimeOffset.Now,
                Interlocked.Increment(ref seq),
                "Tarjeta",
                "scripted",
                play.ToString(),
                $"aceptada={play.Accepted};nombre={play.Name}"));
        }

        stepRecorder.Record(new TransitionStep(
            Interlocked.Increment(ref seq),
            DateTimeOffset.Now,
            "Reactor",
            ledger.GoalMet ? RunStatus.Succeeded.ToString() : RunStatus.Failed.ToString(),
            ledger.Outcome ?? ledger.PendingDebt() ?? "sin resultado"));

        var result = ledger.GoalMet
            ? (RunResult)new RunResult.Success(ledger.Outcome ?? "secuencia completada")
            : new RunResult.Failure(ledger.PendingDebt() ?? "la secuencia no cerro la transaccion");

        var delay = options.DeadlineSeconds > 0 ? TimeSpan.FromMilliseconds(50) : TimeSpan.Zero;
        if (delay > TimeSpan.Zero)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        var journey = new Journey(
            run.Id,
            run.Objective,
            ledger.GoalMet ? RunStatus.Succeeded : RunStatus.Failed,
            ledger.Outcome ?? "sin outcome",
            startedAt,
            DateTimeOffset.Now,
            DateTimeOffset.Now - startedAt,
            stepRecorder.Steps,
            metrics.Snapshot(),
            result.IsSuccess ? null : (result as RunResult.Failure)?.Reason);

        if (_store is not null)
        {
            var snapshot = new RunSnapshot(
                RunSnapshot.CurrentVersion,
                run.Id,
                run.Objective,
                journey.FinalStatus,
                journey.FailureReason,
                journey.StartedAt,
                journey.EndedAt,
                journey.Steps,
                journey.Metrics);
            await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
        }

        return (result, journey);
    }
}
