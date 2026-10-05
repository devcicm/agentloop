using System.Text.Json;
namespace AgentLoop.Tools;


/// <summary>
/// Self-test del IdempotentTool: cachea por hash, sirve cache sin reejecutar,
/// mantiene contador de hits/misses, y maneja correctamente el caso de fallo
/// (no cachea errores).
/// </summary>
public static class IdempotentToolSelfTest
{
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        // Tool que cuenta invocaciones: sirve para medir si el cache evita
        // ejecuciones reales.
        var calls = 0;
        var inner = new DelegateTool(
            "echo",
            "Devuelve el argumento tal cual",
            """{"type":"object","properties":{"x":{"type":"string"}}}""",
            (arguments, _) =>
            {
                Interlocked.Increment(ref calls);
                return ValueTask.FromResult(JsonSerializer.Serialize(new { echoed = arguments.GetProperty("x").GetString() }));
            });

        var wrapper = new IdempotentTool(inner);

        // Primera llamada: ejecuta el inner.
        var args1 = JsonDocument.Parse("""{"x":"hola"}""").RootElement;
        var r1 = wrapper.InvokeAsync(args1, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        results.Add(("primera llamada ejecuta", calls == 1));
        results.Add(("primera llamada devuelve el valor", r1.Contains("\"echoed\":\"hola\"")));

        // Segunda llamada con mismos args: sirve cache, no ejecuta.
        var r2 = wrapper.InvokeAsync(args1, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        results.Add(("segunda llamada sirve cache", calls == 1));
        results.Add(("segunda llamada devuelve el mismo valor", r2 == r1));

        // Tercera llamada con args distintos: ejecuta.
        var args2 = JsonDocument.Parse("""{"x":"adios"}""").RootElement;
        var r3 = wrapper.InvokeAsync(args2, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        results.Add(("tercera llamada con args distintos ejecuta", calls == 2));
        results.Add(("tercera llamada devuelve valor distinto", r3 != r1 && r3.Contains("\"adios\"")));

        // Hits/Misses counters.
        results.Add(("Hits contabilizados correctamente", wrapper.Hits == 1));
        results.Add(("Misses contabilizados correctamente", wrapper.Misses == 2));

        // Cache compartido: dos wrappers con la misma cache comparten resultados.
        var sharedCache = new IdempotencyCache(capacity: 4);
        var w1 = new IdempotentTool(inner, sharedCache);
        var w2 = new IdempotentTool(inner, sharedCache);
        var sharedArgs = JsonDocument.Parse("""{"x":"compartido"}""").RootElement;
        w1.InvokeAsync(sharedArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        w2.InvokeAsync(sharedArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        // El inner se llamo una sola vez: w1 fue miss, w2 fue hit.
        results.Add(("cache compartido entre wrappers", calls == 3));

        // Concurrencia: 64 hilos con la misma clave, inner se ejecuta una vez.
        var concurrente = 0;
        var concurrentInner = new DelegateTool(
            "concurrent",
            "tool para stress",
            """{"type":"object","properties":{"k":{"type":"integer"}}}""",
            (_, _) =>
            {
                Interlocked.Increment(ref concurrente);
                return ValueTask.FromResult("{}");
            });
        var concurrentCache = new IdempotencyCache(capacity: 256);
        var concurrentWrapper = new IdempotentTool(concurrentInner, concurrentCache);
        var cArgs = JsonDocument.Parse("""{"k":42}""").RootElement;
        Parallel.For(0, 64, _ =>
        {
            concurrentWrapper.InvokeAsync(cArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        });
        results.Add(("64 hilos en paralelo: inner ejecuta 1 vez", concurrente == 1));
        results.Add(("64 hilos: hits + misses = 64", concurrentWrapper.Hits + concurrentWrapper.Misses == 64));

        // Fallo async: la clave se libera y el reintento ejecuta de nuevo
        // (agentico: registrar despues del efecto, o liberar si falla).
        var attempts = 0;
        var flakyInner = new DelegateTool(
            "flaky",
            "falla la primera vez",
            """{"type":"object","properties":{}}""",
            (_, _) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    return new ValueTask<string>(Task.FromException<string>(new InvalidOperationException("boom")));
                }

                return ValueTask.FromResult("{}");
            });
        var flakyWrapper = new IdempotentTool(flakyInner);
        var flakyArgs = JsonDocument.Parse("""{}""").RootElement;
        var threw = false;
        try
        {
            flakyWrapper.InvokeAsync(flakyArgs, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }

        results.Add(("fallo lanza en vez de quedarse pegado", threw));
        var recovered = flakyWrapper.InvokeAsync(flakyArgs, CancellationToken.None).GetAwaiter().GetResult();
        results.Add(("fallo libera la clave: reintento ejecuta y devuelve", attempts == 2 && recovered == "{}"));

        // TTL: expirado cuenta como miss y vuelve a ejecutar.
        var ttlCache = new IdempotencyCache(capacity: 4, ttl: TimeSpan.FromMilliseconds(50));
        var ttlCalls = 0;
        var ttlInner = new DelegateTool(
            "ttl",
            "cuenta llamadas",
            """{"type":"object","properties":{}}""",
            (_, _) =>
            {
                Interlocked.Increment(ref ttlCalls);
                return ValueTask.FromResult("v");
            });
        var ttlWrapper = new IdempotentTool(ttlInner, ttlCache);
        var ttlArgs = JsonDocument.Parse("""{}""").RootElement;
        ttlWrapper.InvokeAsync(ttlArgs, CancellationToken.None).GetAwaiter().GetResult();
        ttlWrapper.InvokeAsync(ttlArgs, CancellationToken.None).GetAwaiter().GetResult();
        results.Add(("dentro del TTL sirve cache", ttlCalls == 1));
        Thread.Sleep(150);
        ttlWrapper.InvokeAsync(ttlArgs, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        results.Add(("TTL expirado vuelve a ejecutar", ttlCalls == 2));

        Console.WriteLine();
        Console.WriteLine("Self-test de IdempotentTool:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }
}
