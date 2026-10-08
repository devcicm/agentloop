using System.Text.RegularExpressions;

namespace AgentLoop.Prompting;


/// <summary>
/// Bolsa de parametros del prompt. Mantiene el orden de insercion para que
/// listarlas sea estable, e higieniza y recorta cada valor al entrar: lo que se
/// guarda aqui es exactamente lo que vera el modelo, sin sorpresas en el render.
/// </summary>
public sealed partial class PromptVariables
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _order = [];
    private readonly Dictionary<string, PromptVariableSpec> _specs = new(StringComparer.OrdinalIgnoreCase);
    public PromptVariableSpec Spec(string name) => _specs.GetValueOrDefault(name) ?? new(MaxCharacters: MaxValueLength);

    public PromptVariables(int maxValueLength = TextSanitizer.DefaultMaxLength)
        => MaxValueLength = maxValueLength;

    public int MaxValueLength { get; }

    public IReadOnlyList<string> Names => _order;

    public int Count => _order.Count;

    public string this[string name] => _values.TryGetValue(name, out var value) ? value : string.Empty;

    public bool Contains(string name) => _values.ContainsKey(name);

    public bool TryGet(string name, out string value) => _values.TryGetValue(name, out value!);

    /// <summary>
    /// Define o reemplaza una variable. El nombre tiene que poder aparecer en un hueco
    /// <c>{{...}}</c>; si no, se rechaza aqui en vez de quedar como variable fantasma
    /// que nunca se interpola.
    /// </summary>
    public PromptVariables Set(string name, string? value, PromptVariableSpec? spec = null)
    {
        if (!NameRegex().IsMatch(name))
        {
            throw new PromptTemplateException(
                $"Nombre de variable invalido: '{name}'. Debe empezar por letra o '_' y seguir con letras, numeros, '_', '.' o '-'.");
        }

        spec ??= new(MaxCharacters: MaxValueLength);
        var clean = TextSanitizer.Plain(value);
        if (spec.Sensitive || DataGuard.Redact(clean) != clean) throw new PromptTemplateException("Los secretos no son variables de prompt.");
        if (spec.MaxCharacters < 1) throw new PromptTemplateException("Limite de variable invalido.");
        if (clean.Length > Math.Min(spec.MaxCharacters, MaxValueLength) && !spec.AllowTruncation)
            throw new PromptTemplateException("La variable excede su contrato; no se truncara silenciosamente.");
        if (spec.Type == PromptValueType.Integer && !long.TryParse(clean, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out _)
            || spec.Type == PromptValueType.Boolean && !bool.TryParse(clean, out _)) throw new PromptTemplateException("Tipo de variable invalido.");
        if (spec.Type == PromptValueType.Json) { try { using var json = System.Text.Json.JsonDocument.Parse(clean); } catch (System.Text.Json.JsonException) { throw new PromptTemplateException("Variable JSON invalida."); } }
        _specs[name] = spec;
        if (_values.TryAdd(name, string.Empty))
        {
            _order.Add(name);
        }

        _values[name] = TextSanitizer.Truncate(clean, Math.Min(spec.MaxCharacters, MaxValueLength));
        return this;
    }

    public bool Remove(string name)
    {
        if (!_values.Remove(name))
        {
            return false;
        }

        _specs.Remove(name);
        _order.RemoveAll(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    /// <summary>Parsea la forma <c>clave=valor</c> que usan --var y el comando 'set'.</summary>
    public PromptVariables SetAssignment(string assignment)
    {
        var separator = assignment.IndexOf('=');
        if (separator <= 0)
        {
            throw new PromptTemplateException($"Se esperaba clave=valor y llego: '{assignment}'.");
        }

        return Set(assignment[..separator].Trim(), assignment[(separator + 1)..]);
    }

    /// <summary>
    /// Variables que el harness rellena solo. Se recalculan en cada turno para que
    /// {{hora}} no se quede congelada en la de arranque.
    /// </summary>
    public PromptVariables WithDefaults(string model, IEnumerable<string> toolNames)
    {
        var now = DateTimeOffset.Now;
        var tools = string.Join(", ", toolNames);

        Set("modelo", model);
        Set("herramientas", tools.Length == 0 ? "(ninguna)" : tools);
        Set("fecha", now.ToString("yyyy-MM-dd"));
        Set("hora", now.ToString("HH:mm"));
        Set("directorio_actual", Environment.CurrentDirectory);
        Set("sistema_operativo", Environment.OSVersion.ToString());

        // Las que el usuario puede pisar pero deben existir para que el render no falle.
        if (!Contains("idioma")) Set("idioma", "espanol");
        if (!Contains("contexto")) Set("contexto", string.Empty);

        return this;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-]*$")]
    private static partial Regex NameRegex();
}
