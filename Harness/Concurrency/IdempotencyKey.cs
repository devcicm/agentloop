using System.Security.Cryptography;
using System.Text;

namespace AgentLoop.Harness.Concurrency;

/// <summary>
/// Hash determinista para identificar de forma unica una llamada a una tool.
/// Mismo (tool + argumentos serializados) = misma clave. Pensado para
/// alimentar el <see cref="IdempotencyCache"/>: si el modelo llama dos veces
/// a <c>leer</c> con los mismos argumentos, el cache devuelve la primera
/// respuesta sin ejecutar el tool.
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

        var bytes = Encoding.UTF8.GetBytes($"{toolName}\u0001{argumentsJson}");
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
}
