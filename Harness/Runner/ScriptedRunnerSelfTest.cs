using AgentLoop.Harness.Core;
using AgentLoop.Harness.Durable;
using AgentLoop.Harness.Runner;
using AgentLoop.Harness.Steps;

namespace AgentLoop.Harness.Runner;

/// <summary>
/// Self-test del ScriptedRunner: verifica que ejecuta la secuencia
/// canonica de tarjetas, que el ledger cierra correctamente, y que el
/// Journey refleja todos los pasos (no solo el TransitionStep final).
/// Asi el flujo end-to-end del harness se ejercita sin depender del
/// modelo, y sirve como test de regresion cuando el modelo de turno
/// no cumple el protocolo.
/// </summary>
public static class ScriptedRunnerSelfTest
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        // --- Secuencia default: cubre el happy path ---
        var tempDir = Path.Combine(Path.GetTempPath(), "agentloop-selftest-" + Guid.NewGuid().ToString("N"));
        var store = new FileRunStore(tempDir);
        var run = Core.Run.FromObjective("probar el scripted runner", 60);
        var runner = new ScriptedRunner(store: store);

        var (result, journey) = runner.RunAsync(run, new RunnerOptions { DeadlineSeconds = 60 }).GetAwaiter().GetResult();

        results.Add(("ScriptedRunner: secuencia default produce exito", result.IsSuccess));
        results.Add(("ScriptedRunner: exit code 0", result.IsSuccess));

        // El Journey lleva 1 transition + N scripted steps
        var expectedSteps = ScriptedRunner.DefaultSequence.Count + 1; // +1 para el transition final
        results.Add(("ScriptedRunner: Journey tiene N+1 Steps", journey.Steps.Count == expectedSteps));

        // El primer Step es la tarjeta abrir, el ultimo es el transition
        var firstStep = journey.Steps[0];
        results.Add(("ScriptedRunner: primer step es BusEventStep de tarjeta", firstStep is BusEventStep));
        var lastStep = journey.Steps[^1];
        results.Add(("ScriptedRunner: ultimo step es Transition", lastStep is TransitionStep));

        // El snapshot se persiste con Version 1
        var snapshotPath = Path.Combine(tempDir, $"{run.Id:N}.json");
        results.Add(("ScriptedRunner: snapshot persistido", File.Exists(snapshotPath)));

        // --- Secuencia que NO cierra: debe producir Failure ---
        var incompleta = new ScriptedRunner.ScriptedStep[]
        {
            new("abrir", new Dictionary<string, string>
            {
                ["objetivo"] = "no cerrar",
                ["plan"] = "dejar pendiente",
            }),
            new("avanzar", new Dictionary<string, string> { ["paso"] = "solo un paso" }),
        };
        var runnerFallido = new ScriptedRunner(incompleta);
        var (fallo, _) = runnerFallido.RunAsync(
            Core.Run.FromObjective("incompleto", 60),
            new RunnerOptions { DeadlineSeconds = 60 }).GetAwaiter().GetResult();

        results.Add(("ScriptedRunner: secuencia sin cerrar produce Failure", !fallo.IsSuccess));
        results.Add(("ScriptedRunner: Failure tiene reason legible", fallo is RunResult.Failure f && f.Reason.Contains("comprobacion")));

        // Limpieza
        try { Directory.Delete(tempDir, recursive: true); } catch { }

        Console.WriteLine();
        Console.WriteLine("Self-test del ScriptedRunner:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }
}
