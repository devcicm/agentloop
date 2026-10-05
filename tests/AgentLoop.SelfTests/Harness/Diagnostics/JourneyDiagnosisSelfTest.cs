using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentLoop.Harness.Core;

public static class JourneyDiagnosisSelfTest
{

    /// <summary>
    /// Self-test sin red ni disco (salvo File.Exists sobre una raiz inexistente):
    /// fixtures sinteticas que cubren cerrado, inanicion estilo lobos, watchdog,
    /// presupuesto, rechazos, eficacia de nudges, salidas con codigo != 0,
    /// pausas entre pasos, archivos referenciados y JSON ilegible.
    /// </summary>
    public static bool Run()
    {
        var results = new List<(string Name, bool Ok)>();

        string Step(int seq, string kind, string text, string at) =>
            $"{{\"Sequence\":{seq},\"At\":\"{at}\",\"Name\":\"bus:{kind}\",\"Elapsed\":\"00:00:00\"," +
            $"\"Data\":{{\"event_sequence\":{seq},\"kind\":\"{kind}\",\"source\":\"test\",\"text\":\"{text}\",\"data\":\"\"}}}}";

        string Snap(string status, string? failure, string objective, params string[] steps) =>
            $"{{\"runId\":\"00000000000000000000000000000001\",\"objective\":\"{objective}\"," +
            $"\"status\":{status},\"outcome\":\"x\",\"failureReason\":{(failure is null ? "null" : $"\"{failure}\"")}," +
            $"\"startedAt\":\"2026-09-11T03:00:00Z\",\"endedAt\":\"2026-09-11T03:05:00Z\"," +
            $"\"steps\":[{string.Join(",", steps)}],\"metrics\":{{}}}}";

        // 1. Cerrado: abrir + avanzar + comprobar + cerrar.
        var closed = JourneyDiagnosis.Analyze(Snap("6",
            null,
            "objetivo cumplido",
            Step(1, "Tarjeta", "abrir (nivel 1) x", "2026-09-11T03:00:00Z"),
            Step(2, "Tarjeta", "avanzar (nivel 1) y", "2026-09-11T03:01:00Z"),
            Step(3, "Tarjeta", "comprobar (nivel 1) z", "2026-09-11T03:02:00Z"),
            Step(4, "Tarjeta", "cerrar (nivel 1) w", "2026-09-11T03:03:00Z")), "/bogus");
        results.Add(("cerrado se clasifica", closed.Classification == "cerrado"));
        results.Add(("cerrado cuenta las 4 tarjetas", closed.Abrir == 1 && closed.Avanzar == 1 && closed.Comprobar == 1 && closed.Cerrar == 1));

        // 2. Inanicion estilo lobos: abrir + 2 avanzar, sin comprobar, deuda pendiente.
        var starved = JourneyDiagnosis.Analyze(Snap("7",
            "1 operacion(es) sin cerrar; juega la tarjeta de comprobacion con evidencia real",
            "Escribe el cantico en notes/x.md",
            Step(1, "Tarjeta", "abrir (nivel 1) x", "2026-09-11T03:00:00Z"),
            Step(2, "Tarjeta", "avanzar (nivel 1) y", "2026-09-11T03:01:00Z"),
            Step(3, "Aviso", "respondiste texto sin usar herramientas ((nada)). Legales ahora: x.", "2026-09-11T03:02:00Z"),
            Step(4, "Tarjeta", "avanzar (nivel 1) z", "2026-09-11T03:03:00Z")), "/bogus");
        results.Add(("inanicion se clasifica", starved.Classification == "inanicion"));
        results.Add(("inanicion cuenta narraciones", starved.Narrations == 1));
        results.Add(("inanicion recomienda verificar", starved.RecommendedAction.Contains("comprobar")));

        // 3. Watchdog.
        var watchdog = JourneyDiagnosis.Analyze(Snap("7",
            "watchdog del deadline: presupuesto de 300s agotado durante una llamada",
            "x",
            Step(1, "Tarjeta", "abrir (nivel 1) x", "2026-09-11T03:00:00Z")), "/bogus");
        results.Add(("watchdog se clasifica", watchdog.Classification == "presupuesto-watchdog"));

        // 4. Presupuesto agotado sin tarjeta.
        var budget = JourneyDiagnosis.Analyze(Snap("7",
            "se agoto el presupuesto de 300s sin tarjeta de cierre",
            "x",
            Step(1, "Aviso", "respondiste texto sin usar herramientas ((nada)).", "2026-09-11T03:00:00Z")), "/bogus");
        results.Add(("presupuesto se clasifica", budget.Classification == "presupuesto-agotado"));

        // 5. Rechazos.
        var rejected = JourneyDiagnosis.Analyze(Snap("7",
            "la tarjeta de cierre nunca se jugo",
            "x",
            Step(1, "Tarjeta", "avanzar RECHAZADA: no hay ninguna operacion abierta", "2026-09-11T03:00:00Z")), "/bogus");
        results.Add(("rechazos se cuentan", rejected.Rejected == 1));
        results.Add(("rechazos se clasifican", rejected.Classification == "rechazos"));

        // 6. Nudge seguido de tarjeta.
        var nudged = JourneyDiagnosis.Analyze(Snap("7",
            "juega la tarjeta de comprobacion",
            "x",
            Step(1, "Aviso", "llevas 3 llamada(s) seguidas. Ejemplo concreto: tarjeta(..)", "2026-09-11T03:00:00Z"),
            Step(2, "Tarjeta", "avanzar (nivel 1) ok", "2026-09-11T03:01:00Z")), "/bogus");
        results.Add(("nudges se cuentan", nudged.Nudges == 1));
        results.Add(("nudge seguido de tarjeta se detecta", nudged.NudgesFollowedByCard == 1));

        // 7. Salida con codigo != 0.
        var nonzero = JourneyDiagnosis.Analyze(Snap("7",
            "x",
            "x",
            Step(1, "ProcesoIniciado", "arranca p1", "2026-09-11T03:00:00Z"),
            Step(2, "ProcesoFin", "termina con codigo 23 tras 5.0s", "2026-09-11T03:00:30Z")), "/bogus");
        results.Add(("procesos se cuentan", nonzero.ProcessStarts == 1 && nonzero.ProcessEnds == 1));
        results.Add(("codigo distinto de cero se cuenta", nonzero.NonZeroExits == 1));
        results.Add(("pausa maxima se mide", nonzero.MaxStepGapSeconds >= 29));

        // 8. Archivos: se extraen de objetivo, no existen con raiz bogus.
        results.Add(("archivos se extraen del objetivo", starved.ReferencedFiles.Contains("notes/x.md")));
        results.Add(("archivos ausentes se reportan vacios", starved.ExistingFiles.Count == 0));

        // 9. Vacio e ilegible no lanzan.
        var empty = JourneyDiagnosis.Analyze("{}", "/bogus");
        results.Add(("vacio da desconocido", empty.Classification == "desconocido" && empty.StepCount == 0));
        var broken = JourneyDiagnosis.Analyze("{no json", "/bogus");
        results.Add(("ilegible no lanza", broken.Classification == "ilegible"));

        // 10. Evidencia del manifiesto: se cuenta lo verificado y se lee el agregado.
        var withEvidence = JourneyDiagnosis.Analyze(
            "{\"runId\":\"00000000000000000000000000000001\",\"objective\":\"x\",\"status\":6,\"outcome\":\"ok\"," +
            "\"failureReason\":null,\"startedAt\":\"2026-09-11T03:00:00Z\",\"endedAt\":\"2026-09-11T03:05:00Z\"," +
            "\"steps\":[],\"metrics\":{}," +
            "\"Evidence\":{\"Files\":[{\"Path\":\"notes/x.md\",\"Bytes\":120,\"Hash\":\"ABCDEF123456\",\"Ok\":true,\"At\":\"2026-09-11T03:04:00Z\"}],\"AggregateHash\":\"0123456789ABCDEF\"}}",
            "/bogus");
        results.Add(("evidencia verificada se cuenta", withEvidence.EvidenceVerified == 1));
        results.Add(("agregado de evidencia se lee", withEvidence.EvidenceAggregate == "0123456789ABCDEF"));
        results.Add(("sin evidencia da cero y vacio", empty.EvidenceVerified == 0 && empty.EvidenceAggregate == string.Empty));

        Console.WriteLine();
        Console.WriteLine("Self-test de diagnostico de journeys:");
        foreach (var (name, ok) in results)
        {
            Console.WriteLine($"  [{(ok ? "ok" : "FALLA")}] {name}");
        }

        return results.All(r => r.Ok);
    }

}
