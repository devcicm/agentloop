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
/// <see cref="GoalRunFactory"/> y ejecuta el coordinador de eventos. Antes de este
/// commit GoalRunner.RunAsync construia su propio Scope, bus, supervisor,
/// ledger, tools, GoalContext, StepRecorder y RunContext inline (~120 lineas);
/// ahora son ~30: pide el setup al factory, configura el reactor con la
/// instrumentacion propia y espera su resultado terminal.
/// </summary>
public sealed class GoalRunner : IRunner
{
    private readonly ILLMClient _client;

    public GoalRunner(ILLMClient client)
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

        if (options.ProjectContractPath is not null)
        {
            var admitted = AgentLoop.Goals.Projects.ProjectContractAdmission.Load(options.ProjectContractPath, options.WorkingDirectory);
            if (run.ProjectContract is not null && run.ProjectContract.ContractHash != admitted.ContractHash)
                throw new InvalidOperationException("El contrato del Run difiere del indicado en RunnerOptions.");
            run = run with { ProjectContract = admitted };
        }
        AgentLoop.Goals.Projects.ProjectContractAdmission.RequireExecutionSupport(run.ProjectContract);

        var policy = new ExecutionPolicy
        {
            WorkingDirectory = Path.GetFullPath(options.WorkingDirectory),
            MaxConcurrent = options.MaxConcurrent,
            AllowShell = options.AllowShell, AllowedPaths = options.AllowedPaths,
            Mode = options.PolicyMode.ToLowerInvariant() switch
            {
                "auto" => PolicyMode.Auto, "simular" => PolicyMode.Simular,
                "confirmar" => PolicyMode.Confirmar,
                _ => throw new InvalidOperationException("Politica desconocida."),
            },
        };
        var scope = Scope.Minimal(policy);
        var setup = GoalRunFactory.BuildWithBusRecording(run, scope, _client, []);

        try
        {
            setup.RunContext.Steps.OnRunStart(run.Id);

            var coordinator = new EventOperationCoordinator(_client, run.Model ?? NvidiaOptions.FallbackModelId, setup.Tools, scope)
            {
                MaxModelCalls = options.MaxIterations,
                MaxTokens = options.MaxTokens,
                Thinking = options.Thinking,
                Trace = line => setup.RunContext.Steps.OnLog(new StructuredLog(
                    DateTimeOffset.Now,
                    LogLevel.Info,
                    "reactor",
                    line)),
                RecordModel = (completion, elapsed) => setup.RunContext.Steps.Record(new ModelStep(
                    setup.RunContext.Steps.NextSequence(), DateTimeOffset.Now, completion.Model ?? run.Model ?? NvidiaOptions.FallbackModelId,
                    completion.Usage.PromptTokens, completion.Usage.CompletionTokens, elapsed,
                    completion.Thinking.Observed ? completion.Thinking.ToString() : null)),
                RecordTool = (tool, output, succeeded, elapsed) => setup.RunContext.Steps.Record(new ToolStep(
                    setup.RunContext.Steps.NextSequence(), DateTimeOffset.Now, tool,
                    string.Empty, output, succeeded, elapsed)),
            };

            var operationResult = await coordinator.RunAsync(setup.GoalContext, cancellationToken).ConfigureAwait(false);

            setup.RunContext.EndedAt = DateTimeOffset.Now;
            var succeeded = operationResult.IsSuccess && setup.Ledger.GoalMet;
            var result = succeeded
                ? (RunResult)new RunResult.Success(setup.Ledger.Outcome ?? "objetivo cumplido")
                : new RunResult.Failure(operationResult.Reason, operationResult.Error);

            setup.RunContext.Status = succeeded ? RunStatus.Succeeded : operationResult.Status == OperationStatus.Cancelled ? RunStatus.Cancelled : RunStatus.Failed;
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
