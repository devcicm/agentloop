using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentLoop.Harness.Core;

/// <summary>
/// Diagnostico post-mortem de un run a partir de su snapshot persistido
/// (artifacts/journeys/&lt;runId&gt;.json). Automatiza el procedimiento de
/// diagnostico por etapas: clasifica el cierre, reconstruye la trayectoria
/// de tarjetas, mide narracion vs herramientas, eficacia de los nudges,
/// salidas de procesos con codigo distinto de cero, pausas entre pasos,
/// archivos referenciados y recomienda la siguiente accion.
///
/// Lee el JSON de forma defensiva (JsonDocument, sin deserializacion
/// tipada): los snapshots viejos o parciales producen "desconocido" en
/// vez de lanzar. Los snapshots no guardan BreakReason ni uso de tokens,
/// asi que la clasificacion entre inanicion y otros fallos es heuristica
/// y el informe lo dice explicitamente.
/// </summary>
public sealed record JourneyDiagnosis(
    string RunId,
    string Objective,
    string Status,
    string Outcome,
    string? FailureReason,
    double ElapsedSeconds,
    int StepCount,
    int Abrir,
    int Avanzar,
    int Comprobar,
    int Cerrar,
    int Abortar,
    int Rejected,
    int Narrations,
    int Nudges,
    int NudgesFollowedByCard,
    int ProcessStarts,
    int ProcessEnds,
    int NonZeroExits,
    double MaxStepGapSeconds,
    string MaxStepGapWhere,
    IReadOnlyList<string> ReferencedFiles,
    IReadOnlyList<string> ExistingFiles,
    int EvidenceVerified,
    string EvidenceAggregate,
    string Classification,
    string RecommendedAction)
{
    /// <summary>
    /// Analiza el JSON de un snapshot tal como lo escribe FileRunStore.
    /// <paramref name="repoRoot"/> se usa solo para comprobar la existencia
    /// de los archivos .md mencionados en el objetivo. Nunca lanza: un JSON
    /// ilegible produce clasificacion "ilegible".
    /// </summary>
    public static JourneyDiagnosis Analyze(string snapshotJson, string repoRoot)
    {
        try
        {
            using var doc = JsonDocument.Parse(snapshotJson);
            return AnalyzeDocument(doc.RootElement, repoRoot ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new JourneyDiagnosis(
                "(desconocido)", string.Empty, "desconocido", string.Empty, null,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, string.Empty,
                [], [], 0, string.Empty, "ilegible",
                "el snapshot no parsea como JSON: revisar el archivo.");
        }
    }

    private static JourneyDiagnosis AnalyzeDocument(JsonElement root, string repoRoot)
    {
        var runId = Str(root, "runId", "(desconocido)");
        var objective = Str(root, "objective", string.Empty);
        var status = StatusName(root);
        var outcome = Str(root, "outcome", string.Empty);
        var failure = StrOrNull(root, "failureReason");
        var elapsed = ReadElapsedSeconds(root);

        var abrir = 0;
        var avanzar = 0;
        var comprobar = 0;
        var cerrar = 0;
        var abortar = 0;
        var rejected = 0;
        var narrations = 0;
        var nudgeIndexes = new List<int>();
        var starts = 0;
        var ends = 0;
        var nonZero = 0;
        var maxGap = 0.0;
        var maxGapWhere = string.Empty;
        var texts = new List<string>();

        var steps = root.TryGetProperty("steps", out var stepsEl) && stepsEl.ValueKind == JsonValueKind.Array
            ? stepsEl.EnumerateArray().ToArray()
            : root.TryGetProperty("Steps", out var stepsEl2) && stepsEl2.ValueKind == JsonValueKind.Array
                ? stepsEl2.EnumerateArray().ToArray()
                : [];
        // Los Data de cada step se necesitan dos veces (conteo y eficacia de
        // nudges); se materializan una vez como (kind, text).
        var kinds = new List<string>();
        foreach (var step in steps)
        {
            var data = step.ValueKind == JsonValueKind.Object && TryProp(step, "data", out var d) && d.ValueKind == JsonValueKind.Object
                ? d
                : (JsonElement?)null;
            var kind = data is null ? string.Empty : Str(data.Value, "kind", string.Empty);
            var text = data is null ? string.Empty : Str(data.Value, "text", string.Empty);
            kinds.Add(kind);
            texts.Add(text);

            if (kind.Equals("Tarjeta", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Contains("RECHAZADA", StringComparison.Ordinal))
                {
                    rejected++;
                    continue;
                }

                switch (CardAction(text))
                {
                    case "abrir": abrir++; break;
                    case "avanzar": avanzar++; break;
                    case "comprobar": comprobar++; break;
                    case "cerrar": cerrar++; break;
                    case "abortar": abortar++; break;
                }
            }
            else if (kind.Equals("Aviso", StringComparison.OrdinalIgnoreCase))
            {
                if (text.Contains("respondiste texto sin usar herramientas", StringComparison.Ordinal))
                {
                    narrations++;
                }

                if (text.Contains("Ejemplo concreto", StringComparison.Ordinal))
                {
                    nudgeIndexes.Add(kinds.Count - 1);
                }
            }
            else if (kind.Equals("ProcesoIniciado", StringComparison.OrdinalIgnoreCase))
            {
                starts++;
            }
            else if (kind.Equals("ProcesoFin", StringComparison.OrdinalIgnoreCase))
            {
                ends++;
                var m = Regex.Match(text, @"codigo (\d+)");
                if (m.Success && m.Groups[1].Value != "0")
                {
                    nonZero++;
                }
            }
        }

        var followed = 0;
        foreach (var i in nudgeIndexes)
        {
            for (var j = i + 1; j < kinds.Count && j <= i + 4; j++)
            {
                if (kinds[j].Equals("Tarjeta", StringComparison.OrdinalIgnoreCase)
                    && !texts[j].Contains("RECHAZADA", StringComparison.Ordinal))
                {
                    followed++;
                    break;
                }
            }
        }

        DateTimeOffset? prev = null;
        string prevName = string.Empty;
        foreach (var step in steps)
        {
            var name = step.ValueKind == JsonValueKind.Object && TryProp(step, "name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? string.Empty
                : string.Empty;
            DateTimeOffset? at = null;
            if (step.ValueKind == JsonValueKind.Object && TryProp(step, "at", out var a))
            {
                if (a.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(a.GetString(), out var parsed))
                {
                    at = parsed;
                }
            }

            if (prev.HasValue && at.HasValue)
            {
                var gap = (at.Value - prev.Value).TotalSeconds;
                if (gap > maxGap)
                {
                    maxGap = gap;
                    maxGapWhere = $"{prevName} -> {name}";
                }
            }

            if (at.HasValue)
            {
                prev = at;
                prevName = name;
            }
        }

        var referenced = Regex.Matches(objective, @"[\w\-./\\]+\.md\b")
            .Select(m => m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var existing = referenced
            .Where(p =>
            {
                try
                {
                    var full = Path.IsPathRooted(p) ? p : Path.Combine(repoRoot, p);
                    return File.Exists(full);
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    return false;
                }
            })
            .ToArray();

        var classification = Classify(status, failure, rejected, comprobar, cerrar, abrir, avanzar);
        var action = Recommend(classification, comprobar, avanzar, abrir, existing.Length, referenced.Length);
        var (evidenceVerified, evidenceAggregate) = ReadEvidence(root);

        return new JourneyDiagnosis(
            runId, objective, status, outcome, failure, elapsed, steps.Length,
            abrir, avanzar, comprobar, cerrar, abortar, rejected,
            narrations, nudgeIndexes.Count, followed,
            starts, ends, nonZero, maxGap, maxGapWhere,
            referenced, existing, evidenceVerified, evidenceAggregate, classification, action);
    }

    private static (int Verified, string Aggregate) ReadEvidence(JsonElement root)
    {
        if (!TryProp(root, "evidence", out var evidence) || evidence.ValueKind != JsonValueKind.Object)
        {
            return (0, string.Empty);
        }

        var verified = 0;
        if (TryProp(evidence, "files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind == JsonValueKind.Object
                    && TryProp(file, "ok", out var ok)
                    && ok.ValueKind == JsonValueKind.True)
                {
                    verified++;
                }
            }
        }

        var aggregate = TryProp(evidence, "aggregateHash", out var agg) && agg.ValueKind == JsonValueKind.String
            ? agg.GetString() ?? string.Empty
            : string.Empty;

        return (verified, aggregate);
    }

    private static string? CardAction(string text)
    {
        var t = text.TrimStart();
        foreach (var name in new[] { "abrir", "avanzar", "comprobar", "cerrar", "abortar" })
        {
            if (t.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith(name + "(", StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return null;
    }

    private static string Classify(
        string status, string? failure, int rejected,
        int comprobar, int cerrar, int abrir, int avanzar)
    {
        if (status.Equals("Succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return "cerrado";
        }

        if (failure is { Length: > 0 })
        {
            if (failure.Contains("watchdog", StringComparison.OrdinalIgnoreCase))
            {
                return "presupuesto-watchdog";
            }

            if (failure.Contains("presupuesto", StringComparison.OrdinalIgnoreCase)
                && failure.Contains("sin tarjeta", StringComparison.OrdinalIgnoreCase))
            {
                return "presupuesto-agotado";
            }
        }

        if (rejected > 0 && comprobar == 0 && cerrar == 0)
        {
            return "rechazos";
        }

        if (!status.Equals("desconocido", StringComparison.OrdinalIgnoreCase))
        {
            return "inanicion";
        }

        return "desconocido";
    }

    private static string Recommend(
        string classification, int comprobar, int avanzar, int abrir,
        int existingFiles, int referencedFiles)
    {
        return classification switch
        {
            "cerrado" => "nada: archivar journey.",
            "presupuesto-watchdog" => "reintentar con deadline mayor o modelo mas rapido; la llamada colgaba.",
            "presupuesto-agotado" => "aumentar --deadline/--max-ciclos o reducir trabajo por ciclo.",
            "rechazos" => "revisar schema y orden de tarjetas; ver motivos de rechazo en los steps.",
            "inanicion" when comprobar == 0 && avanzar > 0
                => "el agente investiga sin verificar: aumentar max-ciclos o forzar comprobar con evidencia.",
            "inanicion" when abrir == 0 && avanzar == 0
                => "el modelo no juega tarjetas: revisar prompt de sistema o probar otro modelo.",
            "inanicion" when referencedFiles > 0 && existingFiles == 0
                => "el archivo referenciado no existe: verificar la ruta antes de reintentar.",
            "inanicion" => "aumentar max-ciclos/deadline.",
            "ilegible" => "revisar el archivo.",
            _ => "revisar el journey completo.",
        };
    }

    private static string StatusName(JsonElement root)
    {
        if (TryProp(root, "status", out var s))
        {
            if (s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var v))
            {
                return Enum.GetName(typeof(RunStatus), v) ?? v.ToString();
            }

            if (s.ValueKind == JsonValueKind.String)
            {
                return s.GetString() ?? "desconocido";
            }
        }

        return "desconocido";
    }

    private static double ReadElapsedSeconds(JsonElement root)
    {
        if (TryProp(root, "startedAt", out var s) && TryProp(root, "endedAt", out var e)
            && s.ValueKind == JsonValueKind.String && e.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(s.GetString(), out var start)
            && DateTimeOffset.TryParse(e.GetString(), out var end))
        {
            return Math.Max(0, (end - start).TotalSeconds);
        }

        if (TryProp(root, "elapsed", out var el) && el.ValueKind == JsonValueKind.String
            && TimeSpan.TryParse(el.GetString(), out var span))
        {
            return Math.Max(0, span.TotalSeconds);
        }

        return 0;
    }

    private static bool TryProp(JsonElement obj, string camel, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(camel, out value))
        {
            return true;
        }

        var pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(pascal, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string Str(JsonElement obj, string name, string fallback)
        => TryProp(obj, name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? fallback
            : fallback;

    private static string? StrOrNull(JsonElement obj, string name)
        => TryProp(obj, name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>
    /// Self-test sin red ni disco (salvo File.Exists sobre una raiz inexistente):
    /// fixtures sinteticas que cubren cerrado, inanicion estilo lobos, watchdog,
    /// presupuesto, rechazos, eficacia de nudges, salidas con codigo != 0,
    /// pausas entre pasos, archivos referenciados y JSON ilegible.
    /// </summary>
    public static bool SelfTest()
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
        var closed = Analyze(Snap("6",
            null,
            "objetivo cumplido",
            Step(1, "Tarjeta", "abrir (nivel 1) x", "2026-09-11T03:00:00Z"),
            Step(2, "Tarjeta", "avanzar (nivel 1) y", "2026-09-11T03:01:00Z"),
            Step(3, "Tarjeta", "comprobar (nivel 1) z", "2026-09-11T03:02:00Z"),
            Step(4, "Tarjeta", "cerrar (nivel 1) w", "2026-09-11T03:03:00Z")), "/bogus");
        results.Add(("cerrado se clasifica", closed.Classification == "cerrado"));
        results.Add(("cerrado cuenta las 4 tarjetas", closed.Abrir == 1 && closed.Avanzar == 1 && closed.Comprobar == 1 && closed.Cerrar == 1));

        // 2. Inanicion estilo lobos: abrir + 2 avanzar, sin comprobar, deuda pendiente.
        var starved = Analyze(Snap("7",
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
        var watchdog = Analyze(Snap("7",
            "watchdog del deadline: presupuesto de 300s agotado durante una llamada",
            "x",
            Step(1, "Tarjeta", "abrir (nivel 1) x", "2026-09-11T03:00:00Z")), "/bogus");
        results.Add(("watchdog se clasifica", watchdog.Classification == "presupuesto-watchdog"));

        // 4. Presupuesto agotado sin tarjeta.
        var budget = Analyze(Snap("7",
            "se agoto el presupuesto de 300s sin tarjeta de cierre",
            "x",
            Step(1, "Aviso", "respondiste texto sin usar herramientas ((nada)).", "2026-09-11T03:00:00Z")), "/bogus");
        results.Add(("presupuesto se clasifica", budget.Classification == "presupuesto-agotado"));

        // 5. Rechazos.
        var rejected = Analyze(Snap("7",
            "la tarjeta de cierre nunca se jugo",
            "x",
            Step(1, "Tarjeta", "avanzar RECHAZADA: no hay ninguna operacion abierta", "2026-09-11T03:00:00Z")), "/bogus");
        results.Add(("rechazos se cuentan", rejected.Rejected == 1));
        results.Add(("rechazos se clasifican", rejected.Classification == "rechazos"));

        // 6. Nudge seguido de tarjeta.
        var nudged = Analyze(Snap("7",
            "juega la tarjeta de comprobacion",
            "x",
            Step(1, "Aviso", "llevas 3 llamada(s) seguidas. Ejemplo concreto: tarjeta(..)", "2026-09-11T03:00:00Z"),
            Step(2, "Tarjeta", "avanzar (nivel 1) ok", "2026-09-11T03:01:00Z")), "/bogus");
        results.Add(("nudges se cuentan", nudged.Nudges == 1));
        results.Add(("nudge seguido de tarjeta se detecta", nudged.NudgesFollowedByCard == 1));

        // 7. Salida con codigo != 0.
        var nonzero = Analyze(Snap("7",
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
        var empty = Analyze("{}", "/bogus");
        results.Add(("vacio da desconocido", empty.Classification == "desconocido" && empty.StepCount == 0));
        var broken = Analyze("{no json", "/bogus");
        results.Add(("ilegible no lanza", broken.Classification == "ilegible"));

        // 10. Evidencia del manifiesto: se cuenta lo verificado y se lee el agregado.
        var withEvidence = Analyze(
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
