namespace AgentLoop.Harness.Composition;


/// <summary>
/// Composicion compartida del harness de operaciones: monta bus con identidad
/// causal, supervisor, tarjetas, registro canonical de tools filtrado por Scope,
/// contexto y captura local de evidencia. GoalRunner usa el actor por eventos;
/// el puerto ILLMClient separa al proveedor del motor.
/// </summary>
public static class GoalRunFactory
{
    /// <summary>Setup completo y listo para ejecutar. Todos los campos son publicos y de solo lectura.</summary>
    /// <summary>
    /// Construye el setup completo. El <paramref name="client"/> se construye fuera porque
    /// depende de configuracion externa (SecretStore); aqui lo recibimos listo.
    /// </summary>
    public static GoalRunSetup Build(
        Core.Run run,
        Scope scope,
        ILLMClient client,
        IReadOnlyList<IRunObserver> observers)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(observers);

        if (run.ProjectContract is { } contract && (scope.Policy.AllowShell || scope.AllowedPaths.Count != 1
            || Path.GetFullPath(scope.AllowedPaths[0]) != contract.Root || scope.Policy.WorkingDirectory != contract.Root))
            throw new InvalidOperationException("Proyecto requiere scope exclusivo en root y shell deshabilitado.");

        var correlation = Correlation.Start();

        var bus = new EventBus { RunId = run.Id, DedupWindow = TimeSpan.Zero, AsyncObservers = true };
        var supervisor = new ProcessSupervisor(bus, scope.Policy);
        var ledger = new CardLedger(CardDeck.Transaccional, new EvidenceManifest());
        // La puerta de aceptaciÃ³n resuelve las rutas del objetivo contra este
        // directorio (el --cwd de la polÃ­tica).
        ledger.WorkingDirectory = scope.Policy.WorkingDirectory;
        ledger.AcceptanceObjective = run.Objective;
        var project = run.ProjectContract is null ? null : new AgentLoop.Goals.Projects.ProjectRuntime(run.ProjectContract, ledger, bus, run.ProjectServices ?? throw new InvalidOperationException("Falta composicion de proyecto."), gate: supervisor.Puerta) { ResumeJournal = run.ResumeJournal };
        var registry = OperationTools.Build(ledger, supervisor, bus).Combine(BuiltInTools.DefaultRegistry());
        if (project is not null) registry = registry.Combine(project.Tools()).Filter(name => name is "tarjeta" or "write_text_file" or "read_text_file" or "code_view" or "edit_text_file" or "list_directory" or "find_files" or "project_validate" or "project_command" or "project_evidence" or "project_note");
        var tools = registry.Filter(scope.AllowsTool).WithScope(scope);
        var goalContext = new GoalContext
        {
            Goal = run.Goal,
            Project = project,
            Bus = bus,
            Ledger = ledger,
            Supervisor = supervisor,
        };

        var stepRecorder = new StepRecorder(run.Id, observers) { AsyncObservers = true };
        var metrics = new MetricsCollector();

        var runContext = new RunContext
        {
            RunId = run.Id,
            Goal = goalContext,
            Correlation = correlation,
            Scope = scope,
            Steps = stepRecorder,
            Metrics = metrics,
            Status = RunStatus.Running,
        };

        var runner = new GoalRunner(client, new DefaultGoalRunSetupFactory());

        return new GoalRunSetup
        {
            Bus = bus,
            Supervisor = supervisor,
            Ledger = ledger,
            Tools = tools,
            GoalContext = goalContext,
            StepRecorder = stepRecorder,
            Metrics = metrics,
            Runner = runner,
            RunContext = runContext,
        };
    }

    /// <summary>
    /// Variante para callers que quieren el setup completo y ademas conectar el
    /// bus al StepRecorder (asi los AgentEvents se vuelven BusEventSteps en el
    /// Journey). Es la combinacion que usa JournalAsync.
    /// </summary>
    public static GoalRunSetup BuildWithBusRecording(
        Core.Run run,
        Scope scope,
        ILLMClient client,
        IReadOnlyList<IRunObserver> observers)
    {
        var setup = Build(run, scope, client, observers);

        setup.Bus.Subscribe(item =>
        {
            try
            {
                setup.StepRecorder.Record(new BusEventStep(
                    setup.StepRecorder.NextSequence(),
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
        });

        return setup;
    }

    /// <summary>
    /// Variante para callers que quieren el setup pero con un OnPublished custom
    /// en el bus (por ejemplo, el comando goal del CLI que pinta eventos con
    /// colores via Log). El caller pasa el callback y el factory cablea el resto.
    /// </summary>
    public static GoalRunSetup BuildWithBusHook(
        Core.Run run,
        Scope scope,
        ILLMClient client,
        IReadOnlyList<IRunObserver> observers,
        Action<AgentEvent> onPublished)
    {
        ArgumentNullException.ThrowIfNull(onPublished);

        var setup = Build(run, scope, client, observers);
        setup.Bus.OnPublished = onPublished;
        return setup;
    }

    /// <summary>
    /// Helper de journal: monta el setup, ejecuta el Run via GoalRunner,
    /// persiste el Snapshot en el RunStore y escribe el Journey en
    /// markdown. Es la composicion completa del comando 'journal' en
    /// un solo call site: el CLI ya no cablea Run + RunStore + Journey
    /// writer, solo invoca este metodo.
    /// </summary>
    public static async Task<(RunResult Result, Journey Journey)> RunWithRunnerAsync(
        Core.Run run,
        RunnerOptions options,
        ILLMClient client,
        IRunStore store,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(store);

        var runner = new GoalRunner(client, new DefaultGoalRunSetupFactory());
        {
            var (result, journey) = await runner.RunAsync(run, options, cancellationToken).ConfigureAwait(false);

            var snapshot = new RunSnapshot(
                RunSnapshot.CurrentVersion,
                run.Id,
                run.Objective,
                journey.FinalStatus,
                journey.FailureReason,
                journey.StartedAt,
                journey.EndedAt,
                journey.Steps,
                journey.Metrics,
                journey.Evidence);

            await store.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            var journalDir = (store as FileRunStore)?.Root ?? SecretStore.ResolveRepositoryRoot();
            var markdownPath = Path.Combine(journalDir, $"{run.Id:N}.md");
            await File.WriteAllTextAsync(markdownPath, JourneyWriter.ToMarkdown(journey), CancellationToken.None).ConfigureAwait(false);

            return (result, journey);
        }
    }
}
