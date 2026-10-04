using System.Text.Json.Serialization;

namespace AgentLoop.Provider;

/// <summary>Estado del razonamiento explicito que se le pide al modelo.</summary>
public enum ThinkingMode
{
    Disabled,
    Enabled,
}

/// <summary>
/// Bloque <c>thinking</c> del payload, tal como lo acepta el gateway:
/// <c>{"type": "enabled", "budget_tokens": 256}</c>. Va en la raiz de la peticion,
/// no dentro de un mensaje.
/// </summary>
public sealed class ThinkingOptions
{
    /// <summary>Apagado explicito: no es lo mismo que omitir el campo, y algunos backends lo distinguen.</summary>
    public static readonly ThinkingOptions Off = new() { Mode = ThinkingMode.Disabled };

    [JsonIgnore]
    public ThinkingMode Mode { get; init; } = ThinkingMode.Enabled;

    [JsonPropertyName("type")]
    public string Type => Mode == ThinkingMode.Enabled ? "enabled" : "disabled";

    /// <summary>Tope de tokens que el modelo puede gastar pensando antes de responder.</summary>
    [JsonPropertyName("budget_tokens")]
    public int? BudgetTokens { get; init; }

    public static ThinkingOptions Enabled(int budgetTokens)
    {
        if (budgetTokens < ThinkingPolicy.MinimumBudgetTokens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budgetTokens),
                budgetTokens,
                $"El presupuesto de thinking debe ser >= {ThinkingPolicy.MinimumBudgetTokens} tokens.");
        }

        return new ThinkingOptions { Mode = ThinkingMode.Enabled, BudgetTokens = budgetTokens };
    }

    public override string ToString()
        => Mode == ThinkingMode.Enabled ? $"enabled(budget={BudgetTokens?.ToString() ?? "-"})" : "disabled";
}

/// <summary>Que hacer con el thinking cuando no se dice explicitamente por modelo.</summary>
public enum ThinkingPreference
{
    /// <summary>Activo salvo en los modelos de los que ya consta que no lo soportan.</summary>
    Auto,

    /// <summary>Activo siempre, aunque el modelo lo haya rechazado antes.</summary>
    Always,

    /// <summary>Nunca se manda el parametro.</summary>
    Never,
}

/// <summary>
/// Decide si una peticion lleva <c>thinking</c> segun el modelo. Es mutable a proposito:
/// cuando el gateway rechaza el parametro con un 400 se apunta el modelo aqui y las
/// siguientes peticiones de la sesion salen ya sin el, en vez de pagar el reintento cada vez.
/// </summary>
public sealed class ThinkingPolicy
{
    public const int DefaultBudgetTokens = 256;

    /// <summary>Por debajo de esto el modelo no llega a razonar nada util y solo gasta presupuesto.</summary>
    public const int MinimumBudgetTokens = 64;

    /// <summary>Margen para la respuesta: pensar 256 tokens y tener 256 para contestar deja algo que leer.</summary>
    public const int AnswerHeadroomTokens = 256;

    private readonly HashSet<string> _unsupported;
    private readonly HashSet<string> _supported;

    public ThinkingPolicy(
        ThinkingPreference preference = ThinkingPreference.Auto,
        int budgetTokens = DefaultBudgetTokens,
        IEnumerable<string>? knownSupported = null,
        IEnumerable<string>? knownUnsupported = null)
    {
        Preference = preference;
        BudgetTokens = budgetTokens;
        _supported = new HashSet<string>(knownSupported ?? [], StringComparer.OrdinalIgnoreCase);
        _unsupported = new HashSet<string>(knownUnsupported ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Se puede cambiar en caliente: el CLI interactivo tiene 'pensar on|off|auto'.</summary>
    public ThinkingPreference Preference { get; set; }

    private int _budgetTokens;

    public int BudgetTokens
    {
        get => _budgetTokens;
        set => _budgetTokens = Math.Max(value, MinimumBudgetTokens);
    }

    /// <summary>Modelos que ya rechazaron el parametro o que el sondeo marco sin soporte.</summary>
    public IReadOnlyCollection<string> Unsupported => _unsupported;

    /// <summary>Modelos con soporte confirmado (sondeo o razonamiento observado en vivo).</summary>
    public IReadOnlyCollection<string> Supported => _supported;

    /// <summary>El bloque a poner en la peticion, o null si a este modelo no se le manda.</summary>
    public ThinkingOptions? For(string modelId) => Preference switch
    {
        ThinkingPreference.Never => null,
        ThinkingPreference.Always => ThinkingOptions.Enabled(BudgetTokens),
        _ => IsUnsupported(modelId) ? null : ThinkingOptions.Enabled(BudgetTokens),
    };

    public bool IsEnabledFor(string modelId) => For(modelId) is not null;

    public bool IsUnsupported(string modelId) => _unsupported.Contains(modelId);

    public bool IsSupported(string modelId) => _supported.Contains(modelId);

    /// <summary>Devuelve true la primera vez que se marca, para poder trazarlo una sola vez.</summary>
    public bool MarkUnsupported(string modelId)
    {
        _supported.Remove(modelId);
        return _unsupported.Add(modelId);
    }

    public bool MarkSupported(string modelId)
    {
        _unsupported.Remove(modelId);
        return _supported.Add(modelId);
    }

    /// <summary>
    /// max_tokens util cuando el thinking esta activo: el presupuesto de razonamiento sale
    /// del mismo bolsillo, asi que sin margen el modelo devuelve content vacio.
    /// </summary>
    public int EnsureHeadroom(int maxTokens, string modelId)
        => IsEnabledFor(modelId) ? Math.Max(maxTokens, BudgetTokens + AnswerHeadroomTokens) : maxTokens;

    public static bool TryParsePreference(string value, out ThinkingPreference preference)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                preference = ThinkingPreference.Auto;
                return true;
            case "on" or "si" or "true" or "1" or "always" or "siempre":
                preference = ThinkingPreference.Always;
                return true;
            case "off" or "no" or "false" or "0" or "never" or "nunca":
                preference = ThinkingPreference.Never;
                return true;
            default:
                preference = ThinkingPreference.Auto;
                return false;
        }
    }

    public static ThinkingPreference ParsePreference(string value)
        => TryParsePreference(value, out var preference)
            ? preference
            : throw new InvalidOperationException($"Valor de --thinking no reconocido: '{value}'. Usa auto, on u off.");

    public override string ToString()
        => $"{Preference.ToString().ToLowerInvariant()} (budget={BudgetTokens}, sin soporte: {_unsupported.Count})";
}
