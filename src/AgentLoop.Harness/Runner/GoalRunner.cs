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
    private readonly IGoalRunSetupFactory _factory;

    public GoalRunner(ILLMClient client, IGoalRunSetupFactory factory)
    {
        _factory = factory;
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
            var admitted = _factory.LoadContract(options.ProjectContractPath, options.WorkingDirectory);
            if (run.ProjectContract is not null && run.ProjectContract.ContractHash != admitted.ContractHash)
                throw new InvalidOperationException("El contrato del Run difiere del indicado en RunnerOptions.");
            run = run with { ProjectContract = admitted };
        }
        if (run.ProjectContract is not null && run.ProjectServices is null) run = run with { ProjectServices = options.ProjectServices ?? throw new InvalidOperationException("Proyecto requiere servicios/perfil instalados por el host.") };
        run = run with { Model = run.Model ?? options.Model ?? throw new InvalidOperationException("El host debe seleccionar el modelo de la operacion.") };
        var project = run.ProjectContract?.Contract;
        if (project is not null) run = run with { Objective = project.Objective, Goal = Goal.Of(project.Objective, project.Budgets.DeadlineSeconds), Budget = TimeSpan.FromSeconds(project.Budgets.DeadlineSeconds) };

        var policy = new ExecutionPolicy
        {
            WorkingDirectory = run.ProjectContract?.Root ?? Path.GetFullPath(options.WorkingDirectory),
            MaxConcurrent = project?.Budgets.MaxConcurrentProcesses ?? options.MaxConcurrent,
            AllowShell = project is null && options.AllowShell, AllowedPaths = run.ProjectContract is { } admittedRoot ? [admittedRoot.Root] : options.AllowedPaths,
            Mode = options.PolicyMode.ToLowerInvariant() switch
            {
                "auto" => PolicyMode.Auto, "simular" => PolicyMode.Simular,
                "confirmar" => PolicyMode.Confirmar,
                _ => throw new InvalidOperationException("Politica desconocida."),
            },
        };
        var scope = Scope.Minimal(policy);
        var setup = _factory.Build(run, scope, _client, []);

        try
        {
            setup.RunContext.Steps.OnRunStart(run.Id);
            setup.RunContext.Metrics.Increment("runs.started");

            var coordinator = new EventOperationCoordinator(_client, run.Model!, setup.Tools, scope, _factory.OperationServices)
            {
                Intent = new(options.Intent, run.Objective, []),
                MaxModelCalls = Math.Min(options.MaxIterations, project?.Budgets.MaxModelCalls ?? options.MaxIterations),
                MaxTokens = Math.Min(options.MaxTokens, project?.Budgets.MaxOutputTokens ?? options.MaxTokens),
                Thinking = options.Thinking,
                ContextEstimator = options.ContextEstimator,
                Trace = line =>
                {
                    setup.RunContext.Steps.OnLog(new StructuredLog(DateTimeOffset.Now, LogLevel.Info, "reactor", line));
                    if (options.Verbose) Console.WriteLine(AgentLoop.Security.DataGuard.Display(line));
                },
                RecordModel = (completion, elapsed) =>
                {
                    setup.RunContext.Metrics.Increment("model.calls");
                    setup.RunContext.Metrics.Increment("model.prompt_units", completion.Usage.PromptTokens);
                    setup.RunContext.Metrics.Increment("model.completion_units", completion.Usage.CompletionTokens);
                    setup.RunContext.Steps.Record(new ModelStep(setup.RunContext.Steps.NextSequence(), DateTimeOffset.Now,
                        completion.Model ?? run.Model!, completion.Usage.PromptTokens, completion.Usage.CompletionTokens, elapsed,
                        completion.Thinking.Observed ? completion.Thinking.ToString() : null));
                },
                RecordTool = (tool, output, succeeded, elapsed) =>
                {
                    setup.RunContext.Metrics.Increment("tool.calls");
                    setup.RunContext.Metrics.Increment(succeeded ? "tool.succeeded" : "tool.failed");
                    setup.RunContext.Steps.Record(new ToolStep(setup.RunContext.Steps.NextSequence(), DateTimeOffset.Now, tool,
                        string.Empty, output, succeeded, elapsed));
                },
            };

            setup.RunContext.Metrics.Increment("operations.started");

            var operationResult = await coordinator.RunAsync(setup.GoalContext, cancellationToken).ConfigureAwait(false);

            setup.RunContext.EndedAt = DateTimeOffset.Now;
            var succeeded = operationResult.IsSuccess && setup.Ledger.GoalMet;
            var result = succeeded
                ? (RunResult)new RunResult.Success(setup.Ledger.Outcome ?? "objetivo cumplido")
                : new RunResult.Failure(operationResult.Reason, operationResult.Error);

            setup.RunContext.Status = succeeded ? RunStatus.Succeeded : operationResult.Status == OperationStatus.Cancelled ? RunStatus.Cancelled : RunStatus.Failed;
            setup.RunContext.Metrics.Increment("operations.completed");
            setup.RunContext.Metrics.Increment("operations." + setup.RunContext.Status.ToString().ToLowerInvariant());
            setup.RunContext.Metrics.Increment("provider.http_attempts", operationResult.HttpAttempts);
            setup.RunContext.Metrics.Increment("inference.units", operationResult.Budget?.ChargedUnits ?? 0);
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
