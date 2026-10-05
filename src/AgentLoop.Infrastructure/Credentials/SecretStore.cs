
namespace AgentLoop.Configuration;


/// <summary>
/// Resuelve secretos locales. Prioridad: NVIDIA_API_KEY, archivo NVIDIA, archivo legado y variable legada.
/// </summary>
public static class SecretStore
{
    public const string ApiKeyFileName = "nvidia-api-key.txt";
    public const string ApiKeyEnvVar = "NVIDIA_API_KEY";
    private const string LegacyApiKeyFileName = "tokenrouter-api-key.txt";
    private const string LegacyApiKeyEnvVar = "TOKENROUTER_API_KEY";
    private const string SecretsDirName = "local-secrets";

    /// <summary>Devuelve la credencial del proveedor NVIDIA, con fallback legado. </summary>
    public static string GetLLMApiKey()
        => TryGetLLMApiKey(out var key, out var reason)
            ? key!
            : throw new InvalidOperationException(reason);

    public static bool TryGetLLMApiKey(out string? apiKey, out string reason)
    {
        var fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnvVar)?.Trim();
        if (!string.IsNullOrEmpty(fromEnv))
        {
            apiKey = fromEnv;
            reason = $"variable de entorno {ApiKeyEnvVar}";
            return true;
        }

        var path = FindSecretsFile(ApiKeyFileName) ?? FindSecretsFile(LegacyApiKeyFileName);
        if (path is not null)
        {
            var fromFile = File.ReadAllText(path).Trim();
            if (fromFile.Length > 0)
            {
                apiKey = fromFile;
                reason = $"archivo {path}";
                return true;
            }
        }

        fromEnv = Environment.GetEnvironmentVariable(LegacyApiKeyEnvVar)?.Trim();
        if (!string.IsNullOrEmpty(fromEnv))
        {
            apiKey = fromEnv;
            reason = $"variable de entorno {LegacyApiKeyEnvVar}";
            return true;
        }

        apiKey = null;
        reason =
            $"No se encontro la API key de NVIDIA.{Environment.NewLine}" +
            $"  1) Pegala en una sola linea en {SecretsDirName}/{ApiKeyFileName}" +
            (path is not null ? $" (el archivo existe pero esta vacio: {path})" : " (el archivo no existe)") +
            $"{Environment.NewLine}  2) O define la variable de entorno {ApiKeyEnvVar}";
        return false;
    }

    /// <summary>Ruta del archivo de secreto, o null si no existe en ningun ancestro.</summary>
    public static string? FindSecretsFile(string fileName)
    {
        var dir = FindSecretsDirectory();
        if (dir is null)
        {
            return null;
        }

        var candidate = Path.Combine(dir, fileName);
        return File.Exists(candidate) ? candidate : null;
    }

    /// <summary>
    /// Sube desde el directorio del binario buscando local-secrets/. Permite encontrar secretos aunque
    /// el directorio actual sea distinto de la raiz del repo.
    /// </summary>
    public static string? FindSecretsDirectory()
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, SecretsDirName);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }

    /// <summary>Raiz del repo (el padre de local-secrets/), o el CWD como ultimo recurso.</summary>
    public static string ResolveRepositoryRoot()
    {
        var secrets = FindSecretsDirectory();
        return secrets is not null
            ? Directory.GetParent(secrets)!.FullName
            : Directory.GetCurrentDirectory();
    }

    /// <summary>Enmascara una key para poder loguearla sin filtrarla.</summary>
    public static string Mask(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return "(vacio)";
        }

        return secret.Length <= 10
            ? new string('*', secret.Length)
            : $"{secret[..6]}...{secret[^4..]}";
    }
}
