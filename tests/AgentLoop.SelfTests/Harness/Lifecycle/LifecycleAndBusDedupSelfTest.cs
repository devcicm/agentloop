namespace AgentLoop.Harness.Lifecycle;


/// <summary>
/// Self-test del RunLifecycleMutex y de la deduplicacion del bus.
/// </summary>
public static class LifecycleAndBusDedupSelfTest
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        // --- RunLifecycleMutex ---
        var runId = Guid.NewGuid();
        var stepRecorder = new Steps.StepRecorder(runId, []);
        var recorder = new LifecycleRecorder(stepRecorder, []);
        var mutex = new RunLifecycleMutex();

        // Created -> Pending es valida; Pending -> Running tambien.
        var t1 = mutex.TryTransitionAsync(runId, recorder, RunLifecycleState.Created, RunLifecycleState.Pending, "queue").GetAwaiter().GetResult();
        var t2 = mutex.TryTransitionAsync(runId, recorder, RunLifecycleState.Pending, RunLifecycleState.Running, "arranque").GetAwaiter().GetResult();
        results.Add(("RunLifecycleMutex acepta Created->Pending", t1.IsAccepted));
        results.Add(("RunLifecycleMutex acepta Pending->Running", t2.IsAccepted));

        var transicionInvalida = mutex.TryTransitionAsync(runId, recorder, RunLifecycleState.Created, RunLifecycleState.Succeeded, "salto").GetAwaiter().GetResult();
        results.Add(("RunLifecycleMutex rechaza transicion invalida", !transicionInvalida.IsAccepted && transicionInvalida.Reason != null));

        // --- Concurrencia: 32 transiciones validas ejecutan sin perdida ni duplicado. ---
        // El mutex serializa la ejecucion de OnLifecycle: dos transiciones
        // no se ejecutan en paralelo. Si todas son validas (mismo from->to
        // desde la tabla estatica), las 32 se aceptan pero se ejecutan en
        // serie; verificamos eso midiendo el solapamiento (maximo de
        // ejecuciones concurrentes <= 1) y contando que las 32 se aceptan.
        var runId2 = Guid.NewGuid();
        var stepRecorder2 = new Steps.StepRecorder(runId2, []);
        var recorder2 = new LifecycleRecorder(stepRecorder2, []);
        var mutex2 = new RunLifecycleMutex();
        var inside = 0;
        var maxInside = 0;
        var aceptadas = 0;
        Parallel.For(0, 32, i =>
        {
            // Interceptor en serie: cada transicion duerme 5ms para que
            // sin serializacion se solaparian. Si maxInside > 1, el mutex
            // no esta cumpliendo su contrato.
            var hooked = new HookedRecorder(recorder2, () =>
            {
                var current = Interlocked.Increment(ref inside);
                if (current > 1) Interlocked.Exchange(ref maxInside, current);
                Thread.Sleep(5);
                Interlocked.Decrement(ref inside);
            });
            var r = mutex2.TryTransitionAsync(runId2, hooked, RunLifecycleState.Created, RunLifecycleState.Pending, $"hilo {i}").GetAwaiter().GetResult();
            if (r.IsAccepted) Interlocked.Increment(ref aceptadas);
        });
        results.Add(("32 transiciones validas: todas aceptadas", aceptadas == 32));
        results.Add(("32 transiciones validas: nunca se solapan", maxInside <= 1));

        // --- Bus dedup ---
        var bus = new EventBus();
        var descarte = 0;
        bus.OnDuplicateDiscarded = _ => Interlocked.Increment(ref descarte);
        bus.Publish(AgentEventKind.Aviso, "test", "hola mundo");
        results.Add(("primer evento se publica", bus.PendingCount == 1 && descarte == 0));

        bus.Publish(AgentEventKind.Aviso, "test", "hola mundo"); // duplicado
        results.Add(("evento identico se descarta", bus.PendingCount == 1 && descarte == 1));

        bus.Publish(AgentEventKind.Aviso, "test", "adios"); // distinto texto
        results.Add(("evento distinto se publica", bus.PendingCount == 2 && descarte == 1));

        bus.Publish(AgentEventKind.Error, "test", "hola mundo"); // mismo texto, distinto kind
        results.Add(("mismo texto con distinto kind se publica", bus.PendingCount == 3 && descarte == 1));

        // 1000 eventos distintos pasan todos.
        for (var i = 0; i < 1000; i++)
        {
            bus.Publish(AgentEventKind.Latido, "test", $"latido {i}");
        }

        results.Add(("1000 eventos distintos pasan todos", bus.DiscardedByDedup == 1));

        // Ventana 0 desactiva dedup.
        var busSinDedup = new EventBus { DedupWindow = TimeSpan.Zero };
        busSinDedup.Publish(AgentEventKind.Aviso, "test", "x");
        busSinDedup.Publish(AgentEventKind.Aviso, "test", "x");
        results.Add(("DedupWindow=0 desactiva deduplicacion", busSinDedup.PendingCount == 2));

        // 32 hilos publicando el mismo evento: solo 1 llega al historial.
        var busStress = new EventBus();
        var stressDiscard = 0;
        busStress.OnDuplicateDiscarded = _ => Interlocked.Increment(ref stressDiscard);
        Parallel.For(0, 32, _ =>
        {
            busStress.Publish(AgentEventKind.Aviso, "stress", "mismo");
        });
        results.Add(("32 hilos publicando lo mismo: solo 1 queda", busStress.PendingCount == 1 && stressDiscard == 31));

        Console.WriteLine();
        Console.WriteLine("Self-test de lifecycle mutex y bus dedup:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }
}
