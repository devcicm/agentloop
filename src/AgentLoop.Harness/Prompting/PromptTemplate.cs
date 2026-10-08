using System.Text;
using AgentLoop.Harness.Functional;
using AgentLoop.Kernel.Projects;
using AgentLoop.Harness.Context;
using System.Text.RegularExpressions;

namespace AgentLoop.Prompting;


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

    public static PromptTemplate Parse(string text, int maxTemplateCharacters = 48000)
    {
        if (text.Length > maxTemplateCharacters || maxTemplateCharacters < 1) throw new PromptTemplateException("Plantilla fuera de presupuesto.");
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
        var result = RenderResult(variables);
        return result.IsOk ? result.Value!.Text : throw new PromptTemplateException(result.Error!.Message);
    }

    public Result<PromptRenderResult, Failure> RenderResult(PromptVariables variables, PromptRenderOptions? options = null, ITokenBudgetEstimator? estimator = null)
    {
        options ??= new(); estimator ??= new Utf8TokenBudgetEstimator();
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            if (options.MaxCharacters < 1 || options.MaxBytes < 1) throw new PromptTemplateException("Presupuesto de render invalido.");
            var missing = MissingVariables(variables);
            if (missing.Count > 0) throw new PromptTemplateException("Faltan variables: " + string.Join(", ", missing));
            var builder = new StringBuilder(); var cursor = 0;
            void Append(string value)
            {
                if ((long)builder.Length + value.Length > options.MaxCharacters) throw new PromptTemplateException("Prompt expandido excede presupuesto de caracteres.");
                builder.Append(value);
            }
            foreach (Match match in PlaceholderRegex().Matches(Text))
            {
                Append(Text[cursor..match.Index]);
                var name = match.Groups[2].Value; var spec = variables.Spec(name); var escaping = ParseEscaping(match.Groups[1].Value);
                if (spec.Sensitive || escaping == PromptEscaping.Raw && !spec.Trusted)
                    throw new PromptTemplateException("Variable sensible o raw no confiable rechazada.");
                var value = variables[name];
                Append(escaping switch { PromptEscaping.Xml => TextSanitizer.Xml(value), PromptEscaping.Json => TextSanitizer.Json(value), PromptEscaping.Raw => value, _ => TextSanitizer.Plain(value) });
                cursor = match.Index + match.Length;
            }
            Append(Text[cursor..]); var text = builder.ToString(); var bytes = Encoding.UTF8.GetByteCount(text);
            if (bytes > options.MaxBytes) throw new PromptTemplateException("Prompt expandido excede presupuesto de bytes.");
            return Result<PromptRenderResult, Failure>.Ok(new(text, new(text.Length, bytes, estimator.Estimate(text), estimator.Version, estimator.Exact, Placeholders.Select(p => new PromptVariableAudit(p.Name,variables.Spec(p.Name).Type,variables.Spec(p.Name).Source,variables.Spec(p.Name).Trusted)).ToArray())));
        }
        catch (PromptTemplateException error) { return Result<PromptRenderResult, Failure>.Fail(new("prompt.invalid", error.Message, correlation)); }
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
