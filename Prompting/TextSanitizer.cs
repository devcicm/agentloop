using System.Text;
using System.Text.Json;

namespace AgentLoop.Prompting;

/// <summary>
/// Higiene de las cadenas que acaban dentro de un prompt o de un payload.
///
/// Un valor que viene de fuera (argumentos, archivos, salida de herramientas, texto
/// pegado por el usuario) puede traer caracteres de control o surrogates huerfanos
/// que revientan la serializacion JSON del request, y signos &amp; &lt; &quot; que rompen
/// los bloques XML que el prompt usa como delimitadores. Aqui se limpia antes de
/// interpolar, no despues de que el proveedor devuelva un 400.
/// </summary>
public static class TextSanitizer
{
    /// <summary>Tope por defecto de un valor interpolado; evita prompts que crecen sin freno.</summary>
    public const int DefaultMaxLength = 8000;

    private const string TruncationMarker = "... [truncado]";

    /// <summary>
    /// Base de todo lo demas: quita caracteres de control y surrogates sin pareja,
    /// y normaliza los saltos de linea a \n. Se aplica incluso en modo Raw, porque
    /// un NUL suelto no es una decision de formato: rompe el JSON pase lo que pase.
    /// </summary>
    public static string Plain(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];

            if (ch == '\r')
            {
                if (i + 1 < value.Length && value[i + 1] == '\n')
                {
                    i++; // \r\n cuenta como un solo salto
                }

                builder.Append('\n');
                continue;
            }

            if (ch is '\n' or '\t')
            {
                builder.Append(ch);
                continue;
            }

            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    builder.Append(ch).Append(value[i + 1]);
                    i++;
                }

                // Surrogate alto sin su pareja: se descarta. Serializarlo lanza
                // ArgumentException en System.Text.Json y tumba la peticion entera.
                continue;
            }

            if (char.IsLowSurrogate(ch) || char.IsControl(ch) || ch is '\uFFFE' or '\uFFFF')
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>Recorta dejando marca visible, para que el modelo sepa que falta texto.</summary>
    public static string Truncate(string? value, int maxLength = DefaultMaxLength)
    {
        var clean = value ?? string.Empty;
        if (maxLength <= 0 || clean.Length <= maxLength)
        {
            return clean;
        }

        var cut = Math.Max(0, maxLength - TruncationMarker.Length);

        // No se corta a mitad de un par de surrogates.
        if (cut > 0 && char.IsHighSurrogate(clean[cut - 1]))
        {
            cut--;
        }

        return clean[..cut] + TruncationMarker;
    }

    /// <summary>
    /// Escapa para que el valor no pueda cerrar ni abrir etiquetas del prompt.
    /// Sin esto, un usuario que escribe &quot;&lt;/contexto&gt;&quot; se sale del bloque y el resto
    /// de su texto pasa a leerse como instrucciones del sistema.
    /// </summary>
    public static string Xml(string? value)
    {
        var clean = Plain(value);
        var builder = new StringBuilder(clean.Length + 16);

        foreach (var ch in clean)
        {
            switch (ch)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&apos;"); break;
                default: builder.Append(ch); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapa como contenido de una cadena JSON, sin las comillas envolventes. Para
    /// prompts que llevan un bloque JSON de ejemplo con valores interpolados dentro.
    /// </summary>
    public static string Json(string? value) => JsonEncodedText.Encode(Plain(value)).ToString();

    /// <summary>Lo mismo pero con las comillas puestas, listo para pegar como valor.</summary>
    public static string JsonLiteral(string? value) => $"\"{Json(value)}\"";

    public static bool IsWellFormedJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Garantiza que lo que se devuelve a un mensaje role=tool sea un objeto JSON.
    /// Una herramienta que devuelve texto suelto (o un stack trace) descoloca a los
    /// modelos que asumen JSON en ese rol; se envuelve en lugar de mandarlo crudo.
    ///
    /// Reglas:
    /// - Objeto JSON no vacio con contenido: pasa tal cual (el modelo recibe la estructura real).
    /// - Objeto vacio {}: no aporta nada, se envuelve en {"text":""}.
    /// - Array JSON valido: se envuelve en {"items":[...]}.
    /// - Primitivo JSON valido (numero, true/false/null, string): se envuelve en {"value":...}.
    /// - Lo que no parsea como JSON: se envuelve en {"text":...} con el valor escapado.
    /// Asi el modelo siempre ve un objeto en role=tool, sin perder semantica cuando
    /// la herramienta devuelve un array o un primitivo.
    /// </summary>
    public static string EnsureJsonObject(string? value)
    {
        var clean = Plain(value);
        if (clean.Length == 0)
        {
            return "{\"text\":\"\"}";
        }

        var trimmed = clean.TrimStart();

        if (trimmed.StartsWith('{'))
        {
            if (IsWellFormedJson(clean))
            {
                if (IsEmptyObject(clean))
                {
                    return "{\"text\":\"\"}";
                }

                return clean;
            }

            return $"{{\"text\":{JsonLiteral(clean)}}}";
        }

        if (trimmed.StartsWith('['))
        {
            if (IsWellFormedJson(clean))
            {
                return $"{{\"items\":{JsonLiteral(clean)}}}";
            }

            return $"{{\"text\":{JsonLiteral(clean)}}}";
        }

        if (IsJsonPrimitive(trimmed))
        {
            return $"{{\"value\":{JsonLiteral(clean)}}}";
        }

        return $"{{\"text\":{JsonLiteral(clean)}}}";
    }

    private static bool IsEmptyObject(string value)
    {
        try
        {
            using var doc = JsonDocument.Parse(value);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.EnumerateObject().MoveNext() == false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsJsonPrimitive(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }

        var first = trimmed[0];
        return first is '"' or '-' or '+' or '0' or '1' or '2' or '3' or '4' or '5' or '6' or '7' or '8' or '9'
            || trimmed.StartsWith("true", StringComparison.Ordinal)
            || trimmed.StartsWith("false", StringComparison.Ordinal)
            || trimmed.StartsWith("null", StringComparison.Ordinal);
    }
}
