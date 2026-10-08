using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{
    private const string ModelsArtifact = "artifacts/agentic-models.json";


    private static NvidiaClient BuildClient(out string keySource, ThinkingPolicy? thinking = null)
    {
        var credential = SecretStore.ResolveCredential();
        if (!credential.IsOk) throw new FailureException(credential.Error!);
        var apiKey = credential.Value!.Value;
        keySource = $"[configurada; valor oculto] (desde {credential.Value.Source})";
        return new NvidiaClient(new NvidiaOptions
        {
            ApiKey = apiKey!,
            AllowedOrigins = new[] { NvidiaOptions.DefaultBaseUrl }.Concat(HarnessPolicyLoader.Load().AllowedProviderOrigins).ToArray(),
            BaseUrl = Environment.GetEnvironmentVariable("NVIDIA_BASE_URL") ?? NvidiaOptions.DefaultBaseUrl,
        })
        {
            ThinkingPolicy = thinking,
            OnThinkingRejected = note => Console.WriteLine($"    (thinking) {note}"),
        };
    }


    /// <summary>
    /// Comprueba en vivo que el modelo responde antes de lanzar una prueba larga.
    /// Un ping minimo (una pregunta de un token, sin thinking) distingue:
    ///   - disponible: el gateway devolvio 200.
    ///   - sin acceso/saldo (401/402/403): la cuenta no puede usarlo.
    ///   - no disponible (404 / model_not_found / no available channel): el
    ///     gateway no lo sirve.
    ///   - transporte (status 0): no se pudo contactar.
    /// No lanza: devuelve el veredicto para que el caller decida.
    /// </summary>
    private static async Task<(bool Available, string Reason)> ProbeModelAvailabilityAsync(
        ILLMClient client, string model, CancellationToken cancellationToken)
    {
        try
        {
            var request = new ChatRequest
            {
                Model = model,
                Messages = [ChatMessage.FromUser("ping")],
                MaxTokens = 8,
                Temperature = 0,
                Thinking = null,
            };

            await client.CreateChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);
            return (true, "el modelo respondio a un ping minimo");
        }
        catch (LLMException ex) when (ex.IsQuotaOrAccess)
        {
            return (false, $"sin acceso o saldo para '{model}': {ex.ProviderMessage}");
        }
        catch (LLMException ex) when (
            ex.StatusCode == 404 ||
            ex.ProviderMessage.Contains("model_not_found", StringComparison.OrdinalIgnoreCase) ||
            ex.ProviderMessage.Contains("No available channel", StringComparison.OrdinalIgnoreCase))
        {
            return (false, $"'{model}' no esta disponible en el gateway: {ex.ProviderMessage}");
        }
        catch (LLMException ex)
        {
            return (false, $"el gateway devolvio [{ex.StatusCode}] {ex.ProviderMessage}");
        }
        catch (OperationCanceledException)
        {
            return (false, "el ping de disponibilidad se cancelo");
        }
        catch (Exception ex)
        {
            return (false, $"fallo al sondear '{model}': {ex.GetType().Name}: {ex.Message}");
        }
    }


    /// <summary>
    /// Politica de razonamiento de esta invocacion. Por defecto 'auto': activo salvo en los
    /// modelos que el ultimo 'discover' marco sin soporte, mas los que lo rechacen en vivo.
    /// </summary>
    private static ThinkingPolicy ResolveThinkingPolicy(string[] args)
    {
        var preference = StringOption(args, "--thinking") is { } raw
            ? ThinkingPolicy.ParsePreference(raw)
            : ThinkingPreference.Auto;

        var budget = IntOption(args, "--thinking-budget", ThinkingPolicy.DefaultBudgetTokens);
        var path = Path.Combine(SecretStore.ResolveRepositoryRoot(), ModelsArtifact);
        var index = ModelCapabilityProbe.LoadCapabilityIndex(CapabilityNames.Thinking, path);

        return new ThinkingPolicy(preference, budget, index.Supported, index.Unsupported);
    }


    /// <summary>Una linea legible con el estado del thinking para el modelo en uso.</summary>
    private static string DescribeThinking(ThinkingPolicy policy, string model)
        => NvidiaModelCatalog.IsGlm53(model)
            ? $"{policy.Preference.ToString().ToLowerInvariant()} -> reasoning_effort=low; clear_thinking=true (GLM 5.3 no garantiza budget numerico ni apagado)"
            : policy.Preference switch
    {
        ThinkingPreference.Never => "off (se omite thinking; el comportamiento depende del modelo)",
        ThinkingPreference.Always => $"on siempre, budget={policy.BudgetTokens}",
        _ when policy.IsSupported(model) => $"auto -> activo, budget={policy.BudgetTokens} (soporte confirmado)",
        _ when policy.IsUnsupported(model) => "auto -> inactivo (este modelo no lo soporta)",
        _ => $"auto -> activo a prueba, budget={policy.BudgetTokens} (sin sondeo previo)",
    };


    /// <summary>--model &gt; primer modelo agentico descubierto &gt; fallback conocido.</summary>
    private static string ResolveModel(string[] args)
    {
        var explicitModel = StringOption(args, "--model");
        if (explicitModel is not null)
        {
            return explicitModel;
        }

        var path = Path.Combine(SecretStore.ResolveRepositoryRoot(), ModelsArtifact);
        if (File.Exists(path))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("agentic_models", out var models) &&
                    models.ValueKind == JsonValueKind.Array && models.GetArrayLength() > 0 &&
                    models[0].TryGetProperty("model_id", out var id) && id.GetString() is { Length: > 0 } modelId)
                {
                    if (!modelId.EndsWith(":free", StringComparison.OrdinalIgnoreCase)
                        && !modelId.EndsWith("-free", StringComparison.OrdinalIgnoreCase))
                    {
                        return modelId;
                    }
                }
            }
            catch (JsonException)
            {
                // artefacto corrupto: se ignora y se usa el fallback
            }
        }

        return NvidiaOptions.FallbackModelId;
    }

}
