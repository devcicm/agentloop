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
    public PromptVariables Set(string name, string? value)
    {
        if (!NameRegex().IsMatch(name))
        {
            throw new PromptTemplateException(
                $"Nombre de variable invalido: '{name}'. Debe empezar por letra o '_' y seguir con letras, numeros, '_', '.' o '-'.");
        }

        if (_values.TryAdd(name, string.Empty))
        {
            _order.Add(name);
        }

        _values[name] = TextSanitizer.Truncate(TextSanitizer.Plain(value), MaxValueLength);
        return this;
    }

    public bool Remove(string name)
    {
        if (!_values.Remove(name))
        {
            return false;
        }

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

        // Las que el usuario puede pisar pero deben existir para que el render no falle.
        if (!Contains("idioma")) Set("idioma", "espanol");
        if (!Contains("contexto")) Set("contexto", string.Empty);

        return this;
    }

    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-]*$")]
    private static partial Regex NameRegex();
}
