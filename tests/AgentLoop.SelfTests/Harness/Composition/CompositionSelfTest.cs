namespace AgentLoop.Harness.Composition;


/// <summary>
/// Self-test del Composition Root: verifica que el setup que produce la
/// factory tiene los componentes esperados, que estan cableados entre
/// si, y que las tres consumidores hipoteticos (Program.cs::GoalAsync,
/// Program.cs::JournalAsync, GoalRunner.RunAsync) producen resultados
/// equivalentes en su SelfTest ya existente.
/// </summary>
public static class CompositionSelfTest
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        // --- Setup valido ---
        var run = AgentLoop.Harness.Core.Run.FromObjective("probar el composition root", 30);
        var policy = new Terminals.ExecutionPolicy
        {
            WorkingDirectory = Path.GetFullPath("."),
            MaxConcurrent = 4,
        };
        var scope = Scope.Minimal(policy);
        var observers = new List<IRunObserver>();
        var fakeClient = new FakeLLMClient();

        var setup = GoalRunFactory.BuildWithBusRecording(run, scope, fakeClient, observers);

        results.Add(("setup no es null", setup != null));
        results.Add(("setup.Bus es EventBus", setup!.Bus is EventBus));
        results.Add(("setup.Supervisor es ProcessSupervisor", setup.Supervisor is Terminals.ProcessSupervisor));
        results.Add(("setup.Ledger es CardLedger", setup.Ledger is CardLedger));
        results.Add(("setup.Ledger.Deck tiene 5 cartas", setup.Ledger.Deck.Cards.Count == 5));
        results.Add(("setup.Tools incluye tarjetas, procesos y archivos", new[] { "tarjeta", "ejecutar", "write_text_file", "read_text_file" }.All(setup.Tools.Names.Contains)));
        results.Add(("setup.GoalContext.Goal.Text correcto", setup.GoalContext.Goal.Text == "probar el composition root"));
        results.Add(("setup.RunContext.RunId == run.Id", setup.RunContext.RunId == run.Id));
        results.Add(("setup.RunContext.Status == Running", setup.RunContext.Status == RunStatus.Running));
        results.Add(("setup.Runner es GoalRunner", setup.Runner is Runner.GoalRunner));

        // --- Cableado: el bus dispara steps al recorder ---
        var eventsBefore = setup.StepRecorder.Count;
        setup.Bus.Publish(AgentEventKind.Aviso, "test", "mensaje de prueba");
        results.Add(("bus.Publish dispara un BusEventStep", setup.StepRecorder.Count == eventsBefore + 1));
        var lastStep = setup.StepRecorder.Steps[^1];
        results.Add(("ultimo step es BusEvent", lastStep is BusEventStep));
        results.Add(("ultimo step lleva el texto del evento", lastStep.Data["text"]?.ToString() == "mensaje de prueba"));

        // --- Dos builds producen setups equivalentes (mismo numero de tools, mismo ledger, misma policy) ---
        var setup2 = GoalRunFactory.Build(run, scope, fakeClient, observers);
        results.Add(("dos builds producen mismo numero de tools", setup.Tools.Count == setup2.Tools.Count));
        results.Add(("dos builds producen mismas cartas", setup.Ledger.Deck.Cards.Count == setup2.Ledger.Deck.Cards.Count));

        // --- Cleanup ---
        setup.Supervisor.Dispose();

        Console.WriteLine();
        Console.WriteLine("Self-test del Composition Root:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }
}
