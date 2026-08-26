using System.Diagnostics;
using System.Text.Json;
using AgentLoop.Parsing;
using AgentLoop.Provider;
using AgentLoop.Tools;

namespace AgentLoop.Discovery;

/// <summary>Salida del sondeo: los modelos agenticos encontrados y todo lo que se probo.</summary>
public sealed record SelectionResult(
    IReadOnlyList<ModelCapabilityReport> Agentic,
    IReadOnlyList<ModelCapabilityReport> AllProbed,
    int CandidatesConsidered,
    int Requested)
{
    public bool ReachedTarget => Agentic.Count >= Requested;
}

/// <summary>
/// Descubre modelos con capacidades agenticas sondeandolos en vivo.
/// Puerto reducido de tokenrouter-client/tokenrouter/probe.py.
///
/// Un modelo cuenta como agentico si pasa accessible + chat + tool_calling;
/// json_mode y system_message se sondean como informacion adicional.
/// </summary>
public sealed class AgenticModelSelector
{
    private static readonly IReadOnlyList<ToolDefinition> WeatherTool = [BuiltInTools.GetWeather.ToDefinition()];

    /// <summary>Presupuesto de la sonda de thinking; el mismo que usa probe.py.</summary>
    private const int ThinkingProbeBudget = 200;

    private readonly ITokenRouterClient _client;
    private readonly Action<string> _log;

    public AgenticModelSelector(ITokenRouterClient client, Action<string>? log = null)
    {
        _client = client;
        _log = log ?? (_ => { });
    }

    /// <summary>Tope de modelos a sondear: el catalogo tiene ~127 y probarlos todos es lento y caro.</summary>
    public int MaxCandidates { get; init; } = 30;

    public async Task<SelectionResult> SelectAsync(int target = 3, CancellationToken cancellationToken = default)
    {
        var models = await _client.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        _log($"Catalogo: {models.Count} modelos visibles para esta key.");

        var candidates = models
            .Where(m => m.SupportsChat && (m.IsText || m.Tags.Length == 0))
            .OrderByDescending(m => m.IsFree) // los free primero: son los unicos accesibles con cuota $0.00
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(MaxCandidates)
            .ToList();

        _log($"Candidatos a sondear: {candidates.Count} (max {MaxCandidates}, free primero).");

        var probed = new List<ModelCapabilityReport>();
        var agentic = new List<ModelCapabilityReport>();

        foreach (var model in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var report = await ProbeAsync(model.Id, cancellationToken).ConfigureAwait(false);
            probed.Add(report);

            if (report.IsAgentic)
            {
                agentic.Add(report);
                _log($"  [OK] {model.Id} -> agentico ({string.Join(", ", report.SupportedNames)})");

                if (agentic.Count >= target)
                {
                    break;
                }
            }
            else
            {
                _log($"  [--] {model.Id} -> descartado: {report.SkipReason}");
            }
        }

        return new SelectionResult(agentic, probed, candidates.Count, target);
    }

    /// <summary>Sondea un modelo. Corta temprano: sin acceso no tiene sentido probar lo demas.</summary>
    public async Task<ModelCapabilityReport> ProbeAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var report = new ModelCapabilityReport(modelId);

        if (!await ProbeAccessibleAsync(modelId, report, cancellationToken).ConfigureAwait(false))
        {
            report.SkipReason = report.Capabilities[CapabilityNames.Accessible].Error ?? "no accesible";
            return report;
        }

        await ProbeChatAsync(modelId, report, cancellationToken).ConfigureAwait(false);
        if (!report.Supports(CapabilityNames.Chat))
        {
            report.SkipReason = "responde pero no produce contenido utilizable";
            return report;
        }

        await ProbeToolCallingAsync(modelId, report, cancellationToken).ConfigureAwait(false);
        if (!report.Supports(CapabilityNames.ToolCalling))
        {
            report.SkipReason = "sin tool calling";
            return report;
        }

        // Informativas: no deciden si el modelo es agentico.
        await ProbeJsonModeAsync(modelId, report, cancellationToken).ConfigureAwait(false);
        await ProbeSystemMessageAsync(modelId, report, cancellationToken).ConfigureAwait(false);
        await ProbeThinkingAsync(modelId, report, cancellationToken).ConfigureAwait(false);

        return report;
    }

    private async Task<bool> ProbeAccessibleAsync(string modelId, ModelCapabilityReport report, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _client.CreateChatCompletionAsync(
                new ChatRequest
                {
                    Model = modelId,
                    Messages = [ChatMessage.FromUser("ping")],
                    MaxTokens = 4,
                },
                cancellationToken).ConfigureAwait(false);

            var parsed = CompletionParser.Parse(raw);
            report.Reachable = true;
            report.BackendModel = parsed.Model;
            report.Record(new CapabilityProbe(
                CapabilityNames.Accessible, true, $"id={parsed.Id} backend={parsed.Model}", stopwatch.Elapsed.TotalMilliseconds, null));
            return true;
        }
        catch (TokenRouterException ex)
        {
            // 403 insufficient_user_quota es lo esperado sin saldo: se descarta el modelo, no la corrida.
            report.Record(new CapabilityProbe(
                CapabilityNames.Accessible, false, string.Empty, stopwatch.Elapsed.TotalMilliseconds,
                $"[{ex.StatusCode}] {Shorten(ex.ProviderMessage)}"));
            return false;
        }
    }

    private async Task ProbeChatAsync(string modelId, ModelCapabilityReport report, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _client.CreateChatCompletionAsync(
                new ChatRequest
                {
                    Model = modelId,
                    Messages = [ChatMessage.FromUser("Reply with the single word OK and nothing else.")],
                    // 300 y no 20: los modelos con reasoning gastan el presupuesto pensando
                    // y devuelven content=null si se les aprieta demasiado.
                    MaxTokens = 300,
                    Temperature = 0,
                },
                cancellationToken).ConfigureAwait(false);

            var parsed = CompletionParser.Parse(raw);
            var supported = !string.IsNullOrWhiteSpace(parsed.Content) || parsed.IsThinkingOnly;
            var evidence = parsed.Content is { Length: > 0 }
                ? $"content=\"{Shorten(parsed.Content, 60)}\""
                : parsed.IsThinkingOnly
                    ? "solo razonamiento, sin content"
                    : $"vacio (finish_reason={parsed.FinishReason})";

            report.Record(new CapabilityProbe(CapabilityNames.Chat, supported, evidence, stopwatch.Elapsed.TotalMilliseconds, null));
        }
        catch (TokenRouterException ex)
        {
            report.Record(new CapabilityProbe(
                CapabilityNames.Chat, false, string.Empty, stopwatch.Elapsed.TotalMilliseconds, $"[{ex.StatusCode}] {Shorten(ex.ProviderMessage)}"));
        }
    }

    private async Task ProbeToolCallingAsync(string modelId, ModelCapabilityReport report, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _client.CreateChatCompletionAsync(
                new ChatRequest
                {
                    Model = modelId,
                    Messages = [ChatMessage.FromUser("What is the weather in Madrid right now? Use the available tool.")],
                    Tools = WeatherTool,
                    ToolChoice = "auto",
                    MaxTokens = 300,
                    Temperature = 0,
                },
                cancellationToken).ConfigureAwait(false);

            var parsed = CompletionParser.Parse(raw);
            var evidence = parsed.HasToolCalls
                ? string.Join(", ", parsed.ToolCalls.Select(t => t.ToString()))
                : $"sin tool_calls (finish_reason={parsed.FinishReason})";

            report.Record(new CapabilityProbe(
                CapabilityNames.ToolCalling, parsed.HasToolCalls, evidence, stopwatch.Elapsed.TotalMilliseconds, null));
        }
        catch (TokenRouterException ex)
        {
            report.Record(new CapabilityProbe(
                CapabilityNames.ToolCalling, false, string.Empty, stopwatch.Elapsed.TotalMilliseconds, $"[{ex.StatusCode}] {Shorten(ex.ProviderMessage)}"));
        }
    }

    private async Task ProbeJsonModeAsync(string modelId, ModelCapabilityReport report, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _client.CreateChatCompletionAsync(
                new ChatRequest
                {
                    Model = modelId,
                    Messages = [ChatMessage.FromUser("Return a JSON object with keys \"city\" and \"country\" for Madrid.")],
                    ResponseFormat = ResponseFormat.JsonObject,
                    MaxTokens = 300,
                    Temperature = 0,
                },
                cancellationToken).ConfigureAwait(false);

            var content = CompletionParser.Parse(raw).Content ?? string.Empty;
            var valid = IsJsonObject(content);
            report.Record(new CapabilityProbe(
                CapabilityNames.JsonMode, valid, valid ? Shorten(content, 80) : $"no parsea: {Shorten(content, 60)}",
                stopwatch.Elapsed.TotalMilliseconds, null));
        }
        catch (TokenRouterException ex)
        {
            report.Record(new CapabilityProbe(
                CapabilityNames.JsonMode, false, string.Empty, stopwatch.Elapsed.TotalMilliseconds, $"[{ex.StatusCode}] {Shorten(ex.ProviderMessage)}"));
        }
    }

    private async Task ProbeSystemMessageAsync(string modelId, ModelCapabilityReport report, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _client.CreateChatCompletionAsync(
                new ChatRequest
                {
                    Model = modelId,
                    Messages =
                    [
                        ChatMessage.FromSystem("You must answer every message with the single word PONG, nothing else."),
                        ChatMessage.FromUser("ping"),
                    ],
                    MaxTokens = 300,
                    Temperature = 0,
                },
                cancellationToken).ConfigureAwait(false);

            var content = CompletionParser.Parse(raw).Content ?? string.Empty;
            var obeyed = content.Contains("PONG", StringComparison.OrdinalIgnoreCase);
            report.Record(new CapabilityProbe(
                CapabilityNames.SystemMessage, obeyed, Shorten(content, 60), stopwatch.Elapsed.TotalMilliseconds, null));
        }
        catch (TokenRouterException ex)
        {
            report.Record(new CapabilityProbe(
                CapabilityNames.SystemMessage, false, string.Empty, stopwatch.Elapsed.TotalMilliseconds, $"[{ex.StatusCode}] {Shorten(ex.ProviderMessage)}"));
        }
    }

    /// <summary>
    /// Manda el parametro thinking y comprueba si vuelve razonamiento. Es la sonda que
    /// decide para que modelos se activa: puerto de _probe_reasoning de probe.py.
    /// Un 400 por el parametro cuenta como "no lo soporta", no como fallo de la corrida.
    /// </summary>
    private async Task ProbeThinkingAsync(string modelId, ModelCapabilityReport report, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var raw = await _client.CreateChatCompletionAsync(
                new ChatRequest
                {
                    Model = modelId,
                    Messages = [ChatMessage.FromUser("What is 17 * 23? Show your work briefly.")],
                    // 400 con presupuesto de 200: hay que dejarle con que responder despues de pensar.
                    MaxTokens = 400,
                    Temperature = 0,
                    Thinking = ThinkingOptions.Enabled(ThinkingProbeBudget),
                },
                cancellationToken).ConfigureAwait(false);

            var thinking = CompletionParser.Parse(raw).Thinking;
            var evidence = thinking.Observed
                ? thinking.ToString()
                : "sin reasoning_content";

            report.Record(new CapabilityProbe(
                CapabilityNames.Thinking, thinking.Observed, evidence, stopwatch.Elapsed.TotalMilliseconds, null));
        }
        catch (TokenRouterException ex)
        {
            // El cliente ya reintenta sin thinking cuando el backend rechaza el campo, asi
            // que llegar aqui significa que fallo por otra cosa.
            report.Record(new CapabilityProbe(
                CapabilityNames.Thinking, false, string.Empty, stopwatch.Elapsed.TotalMilliseconds, $"[{ex.StatusCode}] {Shorten(ex.ProviderMessage)}"));
        }
    }

    /// <summary>Lo que el ultimo sondeo sabe de una capacidad, por modelo.</summary>
    public sealed record CapabilityIndex(IReadOnlySet<string> Supported, IReadOnlySet<string> Unsupported)
    {
        public static CapabilityIndex Empty { get; } = new(
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        public int Known => Supported.Count + Unsupported.Count;
    }

    /// <summary>
    /// Lee del artefacto que modelos pasaron o fallaron una capacidad. Es lo que deja al
    /// harness activar el thinking donde hay evidencia sin volver a sondear en cada arranque.
    /// Un modelo que nunca se sondeo no aparece en ninguno de los dos conjuntos: no saber
    /// no es lo mismo que saber que no.
    /// </summary>
    public static CapabilityIndex LoadCapabilityIndex(string capability, string artifactPath)
    {
        if (!File.Exists(artifactPath))
        {
            return CapabilityIndex.Empty;
        }

        var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unsupported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(artifactPath));

            foreach (var section in new[] { "agentic_models", "all_probed" })
            {
                if (!document.RootElement.TryGetProperty(section, out var models) ||
                    models.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var model in models.EnumerateArray())
                {
                    if (model.TryGetProperty("model_id", out var id) is false ||
                        id.GetString() is not { Length: > 0 } modelId ||
                        model.TryGetProperty("capabilities", out var capabilities) is false ||
                        capabilities.ValueKind != JsonValueKind.Object ||
                        capabilities.TryGetProperty(capability, out var probe) is false ||
                        probe.TryGetProperty("supported", out var flag) is false)
                    {
                        continue;
                    }

                    (flag.ValueKind == JsonValueKind.True ? supported : unsupported).Add(modelId);
                }
            }
        }
        catch (JsonException)
        {
            // artefacto corrupto: se prefiere no saber a inventarse soporte
            return CapabilityIndex.Empty;
        }

        // Una respuesta positiva pesa mas que una negativa vieja del mismo artefacto.
        unsupported.ExceptWith(supported);
        return new CapabilityIndex(supported, unsupported);
    }

    /// <summary>Guarda el reporte completo. Devuelve la ruta escrita.</summary>
    public static async Task<string> SaveAsync(SelectionResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var payload = new
        {
            generated_at = DateTimeOffset.UtcNow.ToString("O"),
            requested = result.Requested,
            found = result.Agentic.Count,
            reached_target = result.ReachedTarget,
            candidates_considered = result.CandidatesConsidered,
            agentic_models = result.Agentic.Select(r => r.ToSerializable()).ToList(),
            all_probed = result.AllProbed.Select(r => r.ToSerializable()).ToList(),
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
        return outputPath;
    }

    private static bool IsJsonObject(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(content.Trim());
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Shorten(string value, int max = 120)
        => value.Length <= max ? value : value[..max] + "...";
}
