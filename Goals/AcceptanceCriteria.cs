using System.Text.RegularExpressions;

namespace AgentLoop.Goals;

/// <summary>
/// Criterios de aceptación derivados del objetivo de una transacción, al
/// estilo agentico (TaskContract): ninguna tarea nace sin criterios; si el
/// objetivo no los declara explícitamente, se derivan de su texto.
///
/// Hoy los criterios son rutas de archivo mencionadas en el objetivo
/// (notes/informe.md, data/salida.json...). Un objetivo sin menciones de
/// archivo no produce criterios y queda gobernado por las reglas existentes
/// del libro mayor (apertura, comprobación obligatoria, cierre en orden).
/// </summary>
public static partial class AcceptanceCriteria
{
    /// <summary>
    /// Extrae las rutas de archivo mencionadas en el texto del objetivo, tal
    /// como aparecen (relativas o absolutas). No toca disco.
    /// </summary>
    public static IReadOnlyList<string> DerivePaths(string objectiveText)
    {
        if (string.IsNullOrWhiteSpace(objectiveText))
        {
            return [];
        }

        return FileMentionRegex()
            .Matches(objectiveText)
            .Select(m => m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// De las rutas mencionadas, devuelve descripciones de las que no tienen
    /// evidencia real: no existen o están vacías. Lista vacía = hay evidencia.
    /// Cada verificación queda registrada en el manifiesto (auditoría).
    /// </summary>
    public static IReadOnlyList<string> MissingEvidence(
        IEnumerable<string> mentionedPaths,
        string workingDirectory,
        EvidenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(mentionedPaths);
        ArgumentNullException.ThrowIfNull(manifest);

        var missing = new List<string>();
        foreach (var mentioned in mentionedPaths)
        {
            string absolute;
            try
            {
                absolute = Path.IsPathRooted(mentioned)
                    ? Path.GetFullPath(mentioned)
                    : Path.GetFullPath(Path.Combine(workingDirectory, mentioned));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                missing.Add($"{mentioned}: ruta no válida");
                continue;
            }

            var entry = manifest.Verify(absolute);
            if (!entry.Ok)
            {
                missing.Add($"{mentioned}: no existe o está vacío");
            }
        }

        return missing;
    }

    [GeneratedRegex("""["'](?<quoted>[^"'\r\n]+\.(?:md|txt|json|csv|log|xml|ya?ml))["']|[a-z]:[\\/][^"'\r\n<>|?*]+?\.(?:md|txt|json|csv|log|xml|ya?ml)\b|[\w\-./\\:]+\.(?:md|txt|json|csv|log|xml|ya?ml)\b""", RegexOptions.IgnoreCase)]
    private static partial Regex FileMentionRegex();
}
