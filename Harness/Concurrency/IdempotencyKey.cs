using System.Security.Cryptography;
using System.Text;

namespace AgentLoop.Harness.Concurrency;

/// <summary>
/// Hash determinista para identificar de forma unica una llamada a una tool.
/// Mismo (tool + argumentos) = misma clave, sin importar el orden de las
/// claves del JSON. Pensado para alimentar el <see cref="IdempotencyCache"/>:
/// si el modelo llama dos veces a <c>leer</c> con los mismos argumentos, el
/// cache devuelve la primera respuesta sin ejecutar el tool.
///
/// Regla de agentico (HARNESS_CONTRACTS): fingerprint canonico, nunca
/// serializacion cruda. <c>JSON.stringify</c> respeta el orden de insercion,
/// asi que el mismo argumento reordenado esquivaria el dedup. Aqui las
/// claves de objeto se ordenan recursivamente antes de hashear (los arrays
/// conservan su orden: el orden de una lista de pasos es significativo).
/// Si el texto no parsea como JSON, se hashea crudo (documentado, no
/// silencioso: el caller ve claves distintas por formato distinto).
///
/// SHA-256 sobre UTF-8: 32 bytes deterministas, sin colisiones relevantes
/// para los pocos cientos de tools que un Run maneja en su vida.
/// </summary>
public static class IdempotencyKey
{
    public static string ComputeKey(string toolName, string argumentsJson)
    {
        ArgumentNullException.ThrowIfNull(toolName);
        ArgumentNullException.ThrowIfNull(argumentsJson);

        string canonical;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(argumentsJson);
            canonical = Canonicalize(doc.RootElement);
        }
        catch (System.Text.Json.JsonException)
        {
            // No es JSON: se hashea el texto tal cual. Determinista para la
            // misma entrada, pero dos formatos distintos no colapsan.
            canonical = argumentsJson;
        }

        var bytes = Encoding.UTF8.GetBytes($"{toolName}\u0001{canonical}");
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public static string ComputeKey(string toolName, IReadOnlyDictionary<string, object?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        // Estabiliza el orden para que dos llamadas con el mismo contenido
        // produzcan la misma clave aunque las claves del diccionario esten
        // en otro orden.
        var ordered = arguments.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var builder = new StringBuilder("{");
        var first = true;
        foreach (var (key, value) in ordered)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append(System.Text.Json.JsonSerializer.Serialize(key));
            builder.Append(':');
            builder.Append(System.Text.Json.JsonSerializer.Serialize(value));
        }

        builder.Append('}');
        return ComputeKey(toolName, builder.ToString());
    }

    /// <summary>
    /// Forma canónica de un JSON: objetos con claves ordenadas recursivamente,
    /// arrays con su orden, primitivos tal cual. Dos textos con el mismo
    /// contenido producen la misma forma aunque cambie el orden o el
    /// espaciado. Los números conservan su texto original ("1" vs "1.0" no
    /// colapsan: caso raro que se documenta en vez de normalizar a ciegas).
    /// </summary>
    internal static string Canonicalize(System.Text.Json.JsonElement element)
    {
        var builder = new StringBuilder();
        AppendCanonical(element, builder);
        return builder.ToString();
    }

    private static void AppendCanonical(System.Text.Json.JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object:
                builder.Append('{');
                var first = true;
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        builder.Append(',');
                    }

                    first = false;
                    builder.Append(System.Text.Json.JsonSerializer.Serialize(property.Name));
                    builder.Append(':');
                    AppendCanonical(property.Value, builder);
                }

                builder.Append('}');
                break;

            case System.Text.Json.JsonValueKind.Array:
                builder.Append('[');
                var head = true;
                foreach (var item in element.EnumerateArray())
                {
                    if (!head)
                    {
                        builder.Append(',');
                    }

                    head = false;
                    AppendCanonical(item, builder);
                }

                builder.Append(']');
                break;

            default:
                builder.Append(element.GetRawText());
                break;
        }
    }
}
