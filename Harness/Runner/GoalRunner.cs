using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Goals;
using AgentLoop.Harness.Core;
using AgentLoop.Harness.Observability;
using AgentLoop.Harness.Steps;
using AgentLoop.Harness.Traceability;
using AgentLoop.Provider;
using AgentLoop.Terminals;
using AgentLoop.Tools;

namespace AgentLoop.Harness.Runner;

/// <summary>
/// Implementacion de <see cref="IRunner"/> que envuelve <see cref="EventDrivenAgent"/>
/// dentro de un Run con Scope, Correlation y StepRecorder. Asi cada ciclo del
/// reactor, cada tool ejecutada y cada evento del bus queda registrado como
/// Step en el Journey, y los counters (cycles, tools, retries, idle_waits)
/// se exponen via MetricsCollector.
///
/// Este commit no enchufa todavia el GoalRunner a Program.cs; ese wiring
/// entra en el commit F (Durable + Program) junto con la persistencia.
/// </summary>
public sealed class GoalRunner : IRunner
{
    private readonly ITokenRouterClient _client;

    public GoalRunner(ITokenRouterClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<(RunResult Result, Journey Journey)> RunAsync(
        Core.Run run,
        RunnerOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(options);

        var correlation = Correlation.Start();
        var policy = new ExecutionPolicy
        {
            WorkingDirectory = Path.GetFullPath(options.WorkingDirectory),
            MaxConcurrent = options.MaxConcurrent,
        };

        var scope = Scope.Minimal(policy);
        var metrics = new MetricsCollector();
        var recorder = new StepRecorder(run.Id, []);

        var ledger = new CardLedger(CardDeck.Transaccional);
        var bus = new EventBus();
        var supervisor = new ProcessSupervisor(bus, policy);
        var tools = OperationTools.Build(ledger, supervisor, bus);

        var goalContext = new GoalContext
        {
            Goal = run.Goal,
            Bus = bus,
            Ledger = ledger,
            Supervisor = supervisor,
        };

        var context = new RunContext
        {
            RunId = run.Id,
            Goal = goalContext,
            Correlation = correlation,
            Scope = scope,
            Steps = recorder,
            Metrics = metrics,
            Status = RunStatus.Running,
        };

        // Wire bus -> Step: cada AgentEvent publicado durante el Run se
        // convierte en un BusEventStep con su Correlation. Asi el Journey
        // del comando journal muestra cada ciclo de narracion, cada aviso
        // y cada tool call, no solo el TransitionStep final.
        bus.OnPublished = item =>
        {
            try
            {
                context.Steps.Record(new BusEventStep(
                    context.Steps.NextSequence(),
                    item.At,
                    item.Sequence,
                    item.Kind.ToString(),
                    item.Source,
                    item.Text,
                    item.Data));
            }
            catch
            {
                // Un step que falla no rompe el reactor.
            }
        };

        context.Steps.OnRunStart(run.Id);

        var machine = new EventDrivenAgent(_client, run.Model ?? "qwen/qwen3.8-max-free", tools)
        {
            Trace = line => context.Steps.OnLog(new StructuredLog(
                DateTimeOffset.Now,
                LogLevel.Info,
                "reactor",
                line)),
        };

        var loop = new Looping.AgentLoopRunner<ReactorState, GoalContext>(machine)
        {
            MaxIterations = options.MaxIterations,
            BreakWhen = iteration => iteration.Context.Ledger.GoalMet,
        };

        try
        {
            var loopResult = await loop.RunAsync(goalContext, cancellationToken).ConfigureAwait(false);
            context.EndedAt = DateTimeOffset.Now;

            var succeeded = loopResult.Succeeded && ledger.GoalMet;
            var result = succeeded
                ? (RunResult)new RunResult.Success(ledger.Outcome ?? "objetivo cumplido")
                : new RunResult.Failure(goalContext.FailureReason ?? ledger.PendingDebt() ?? "operacion no cerrada", loopResult.Error);

            context.Status = succeeded ? RunStatus.Succeeded : RunStatus.Failed;

            context.Steps.Record(new TransitionStep(
                context.Steps.NextSequence(),
                DateTimeOffset.Now,
                "Reactor",
                context.Status.ToString(),
                result.ToString()));

            var journey = BuildJourney(context, run, result);
            context.Steps.OnRunEnd(run.Id, result.ToString());
            return (result, journey);
        }
        catch (OperationCanceledException)
        {
            context.EndedAt = DateTimeOffset.Now;
            context.Status = RunStatus.Cancelled;
            var result = new RunResult.Failure("cancelado por el usuario o por el budget");
            var journey = BuildJourney(context, run, result);
            context.Steps.OnRunEnd(run.Id, result.ToString());
            return (result, journey);
        }
        catch (Exception ex)
        {
            context.EndedAt = DateTimeOffset.Now;
            context.Status = RunStatus.Failed;
            context.Steps.Record(new ErrorStep(
                context.Steps.NextSequence(),
                DateTimeOffset.Now,
                "runner",
                ex.Message,
                Transient: false));
            var result = new RunResult.Failure(ex.Message, ex);
            var journey = BuildJourney(context, run, result);
            context.Steps.OnRunEnd(run.Id, result.ToString());
            return (result, journey);
        }
        finally
        {
            supervisor.Dispose();
        }
    }

    private static Journey BuildJourney(RunContext context, Core.Run run, RunResult result)
    {
        var outcome = result switch
        {
            RunResult.Success s => s.Outcome,
            RunResult.Failure f => f.Reason,
            _ => "desconocido",
        };

        return new Journey(
            run.Id,
            run.Objective,
            context.Status,
            outcome,
            context.StartedAt,
            context.EndedAt,
            context.Elapsed,
            context.Steps.Steps,
            context.Metrics.Snapshot(),
            context.FailureReason);
    }
}
