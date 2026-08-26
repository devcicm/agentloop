using System.Text.RegularExpressions;

namespace AgentLoop.Prompting;

/// <summary>Como se escapa un valor al interpolarlo.</summary>
public enum PromptEscaping
{
    /// <summary>Solo limpieza de caracteres de control. Por defecto.</summary>
    Plain,

    /// <summary>Escapa &amp; &lt; &gt; &quot; &apos;: para valores dentro de bloques etiquetados.</summary>
    Xml,

    /// <summary>Escapa como contenido de cadena JSON, sin comillas.</summary>
    Json,

    /// <summary>Sin escapar. Solo para plantillas anidadas de confianza.</summary>
    Raw,
}

public readonly record struct PromptPlaceholder(string Name, PromptEscaping Escaping);

/// <summary>Se lanza cuando la plantilla pide variables que nadie definio.</summary>
public sealed class PromptTemplateException(string message) : InvalidOperationException(message);

/// <summary>
/// Plantilla de prompt con interpolacion <c>{{variable}}</c>.
///
/// El modo de escapado va en el propio hueco: <c>{{xml:contexto}}</c>, <c>{{json:payload}}</c>,
/// <c>{{raw:plantilla}}</c>. Sin prefijo se asume <see cref="PromptEscaping.Plain"/>.
///
/// Lo que no encaje con la forma de un hueco se deja tal cual, para que una plantilla
/// pueda contener JSON con llaves sin que haya que escaparlo.
/// </summary>
public sealed partial class PromptTemplate
{
    private PromptTemplate(string text, IReadOnlyList<PromptPlaceholder> placeholders)
    {
        Text = text;
        Placeholders = placeholders;
    }

    public string Text { get; }

    /// <summary>Huecos encontrados, en orden de aparicion y sin repetidos.</summary>
    public IReadOnlyList<PromptPlaceholder> Placeholders { get; }

    public static PromptTemplate Parse(string text)
    {
        var clean = TextSanitizer.Plain(text);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var placeholders = new List<PromptPlaceholder>();

        foreach (Match match in PlaceholderRegex().Matches(clean))
        {
            var name = match.Groups[2].Value;
            if (seen.Add(name))
            {
                placeholders.Add(new PromptPlaceholder(name, ParseEscaping(match.Groups[1].Value)));
            }
        }

        return new PromptTemplate(clean, placeholders);
    }

    /// <summary>Nombres que la plantilla necesita y las variables no traen.</summary>
    public IReadOnlyList<string> MissingVariables(PromptVariables variables)
        => [.. Placeholders.Where(p => !variables.Contains(p.Name)).Select(p => p.Name)];

    /// <summary>
    /// Sustituye los huecos. Falla si falta alguna variable en vez de mandar un
    /// <c>{{modelo}}</c> literal al proveedor: un prompt a medio interpolar produce
    /// respuestas raras que luego parecen culpa del modelo.
    /// </summary>
    public string Render(PromptVariables variables)
    {
        var missing = MissingVariables(variables);
        if (missing.Count > 0)
        {
            var known = variables.Names.Count == 0 ? "(ninguna)" : string.Join(", ", variables.Names);
            throw new PromptTemplateException(
                $"Faltan variables en la plantilla: {string.Join(", ", missing)}. Definidas: {known}.");
        }

        return PlaceholderRegex().Replace(Text, match =>
        {
            var value = variables[match.Groups[2].Value];
            return ParseEscaping(match.Groups[1].Value) switch
            {
                PromptEscaping.Xml => TextSanitizer.Xml(value),
                PromptEscaping.Json => TextSanitizer.Json(value),
                PromptEscaping.Raw => value,
                _ => TextSanitizer.Plain(value),
            };
        });
    }

    private static PromptEscaping ParseEscaping(string prefix) => prefix.ToLowerInvariant() switch
    {
        "xml" => PromptEscaping.Xml,
        "json" => PromptEscaping.Json,
        "raw" => PromptEscaping.Raw,
        _ => PromptEscaping.Plain,
    };

    [GeneratedRegex(@"\{\{\s*(?:(raw|plain|xml|json)\s*:\s*)?([A-Za-z_][A-Za-z0-9_.\-]*)\s*\}\}")]
    private static partial Regex PlaceholderRegex();
}
