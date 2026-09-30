using AgentLoop.Cards;
using AgentLoop.Events;
using AgentLoop.Goals;
using AgentLoop.Harness.Composition;
using AgentLoop.Harness.Core;
using AgentLoop.Harness.Observability;
using AgentLoop.Harness.Steps;
using AgentLoop.Harness.Traceability;
using AgentLoop.Provider;
using AgentLoop.Terminals;
using AgentLoop.Tools;

namespace AgentLoop.Harness.Runner;

/// <summary>
/// Implementacion de <see cref="IRunner"/> que delega el setup completo al
/// <see cref="GoalRunFactory"/> y se centra en ejecutar el loop. Antes de este
/// commit GoalRunner.RunAsync construia su propio Scope, bus, supervisor,
/// ledger, tools, GoalContext, StepRecorder y RunContext inline (~120 lineas);
/// ahora son ~30: pide el setup al factory, configura el reactor con la
/// instrumentacion propia y ejecuta el loop.
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

        var policy = new ExecutionPolicy
        {
            WorkingDirectory = Path.GetFullPath(options.WorkingDirectory),
            MaxConcurrent = options.MaxConcurrent,
        };
        var scope = Scope.Minimal(policy);
        var setup = GoalRunFactory.BuildWithBusRecording(run, scope, _client, []);

        try
        {
            setup.RunContext.Steps.OnRunStart(run.Id);

            var machine = new EventDrivenAgent(_client, run.Model ?? TokenRouterOptions.FallbackModelId, setup.Tools)
            {
                MaxTokens = options.MaxTokens,
                Thinking = options.Thinking,
                Trace = line => setup.RunContext.Steps.OnLog(new StructuredLog(
                    DateTimeOffset.Now,
                    LogLevel.Info,
                    "reactor",
                    line)),
                OnModelCall = (completion, elapsed) => setup.RunContext.Steps.Record(new ModelStep(
                    setup.RunContext.Steps.NextSequence(), DateTimeOffset.Now, completion.Model ?? run.Model ?? TokenRouterOptions.FallbackModelId,
                    completion.Usage.PromptTokens, completion.Usage.CompletionTokens, elapsed,
                    completion.Thinking.Observed ? completion.Thinking.ToString() : null)),
                OnToolCall = (tool, output, succeeded, elapsed) => setup.RunContext.Steps.Record(new ToolStep(
                    setup.RunContext.Steps.NextSequence(), DateTimeOffset.Now, tool,
                    string.Empty, output, succeeded, elapsed)),
            };

            var loop = new Looping.AgentLoopRunner<ReactorState, GoalContext>(machine)
            {
                // options.MaxIterations son ciclos del reactor (llamadas al
                // modelo), no iteraciones del loop: cada ciclo consume varias
                // iteraciones (razonar, esperar, despachar). Se evalua el tope
                // de ciclos en BreakWhen y MaxIterations es la red de seguridad.
                MaxIterations = Math.Max(options.MaxIterations * 4, options.MaxIterations + 8),
                BreakWhen = iteration =>
                    iteration.Context.Ledger.GoalMet
                    || iteration.Context.Ledger.Aborted
                    || iteration.Context.Expired
                    || (iteration.Context.Cycles >= options.MaxIterations && iteration.State != ReactorState.Despachando),
            };

            var loopResult = await loop.RunAsync(setup.GoalContext, cancellationToken).ConfigureAwait(false);

            setup.RunContext.EndedAt = DateTimeOffset.Now;
            var succeeded = loopResult.Result.IsOk && setup.Ledger.GoalMet;
            var result = succeeded
                ? (RunResult)new RunResult.Success(setup.Ledger.Outcome ?? "objetivo cumplido")
                : new RunResult.Failure(setup.GoalContext.FailureReason ?? setup.Ledger.PendingDebt() ?? "operacion no cerrada", loopResult.Error);

            setup.RunContext.Status = succeeded ? RunStatus.Succeeded : RunStatus.Failed;
            setup.RunContext.FailureReason = result.IsSuccess ? null : (result as RunResult.Failure)?.Reason;

            setup.RunContext.Steps.Record(new TransitionStep(
                setup.RunContext.Steps.NextSequence(),
                DateTimeOffset.Now,
                "Reactor",
                setup.RunContext.Status.ToString(),
                result.ToString()));

            var journey = BuildJourney(setup.RunContext, run, result);
            setup.RunContext.Steps.OnRunEnd(run.Id, result.ToString());
            return (result, journey);
        }
        catch (OperationCanceledException)
        {
            setup.RunContext.EndedAt = DateTimeOffset.Now;
            setup.RunContext.Status = RunStatus.Cancelled;
            var result = new RunResult.Failure("cancelado por el usuario o por el budget");
            var journey = BuildJourney(setup.RunContext, run, result);
            setup.RunContext.Steps.OnRunEnd(run.Id, result.ToString());
            return (result, journey);
        }
        catch (Exception ex)
        {
            setup.RunContext.EndedAt = DateTimeOffset.Now;
            setup.RunContext.Status = RunStatus.Failed;
            setup.RunContext.Steps.Record(new ErrorStep(
                setup.RunContext.Steps.NextSequence(),
                DateTimeOffset.Now,
                "runner",
                ex.Message,
                Transient: false));
            var result = new RunResult.Failure(ex.Message, ex);
            var journey = BuildJourney(setup.RunContext, run, result);
            setup.RunContext.Steps.OnRunEnd(run.Id, result.ToString());
            return (result, journey);
        }
        finally
        {
            setup.Supervisor.Dispose();
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
            context.FailureReason,
            context.Goal.Ledger.Evidence.Snapshot());
    }
}
