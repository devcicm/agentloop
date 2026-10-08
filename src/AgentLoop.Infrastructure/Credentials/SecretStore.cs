using AgentLoop.Harness.Functional;

namespace AgentLoop.Configuration;


/// <summary>
/// Resuelve secretos locales. Prioridad: NVIDIA_API_KEY y archivo NVIDIA. No mezcla credenciales de proveedores.
/// </summary>
public static class SecretStore
{
    public const string ApiKeyFileName = "nvidia-api-key.txt";
    public const string ApiKeyEnvVar = "NVIDIA_API_KEY";
    private const string SecretsDirName = "local-secrets";

    /// <summary>Devuelve la credencial del proveedor NVIDIA, sin fallback entre proveedores. </summary>
    public static string GetLLMApiKey()
        => ResolveCredential().Match(value => value.Value, failure => throw new FailureException(failure));

    public static bool TryGetLLMApiKey(out string? apiKey, out string reason)
    {
        var fromEnv = Environment.GetEnvironmentVariable(ApiKeyEnvVar)?.Trim();
        if (!string.IsNullOrEmpty(fromEnv))
        {
            apiKey = fromEnv;
            reason = $"variable de entorno {ApiKeyEnvVar}";
            return true;
        }

        var path = FindSecretsFile(ApiKeyFileName);
        if (path is not null)
        {
            if (new FileInfo(path).Length > 4096) throw new FailureException(new("credentials.malformed", "Archivo de credencial demasiado grande. Usa setup.", Guid.NewGuid().ToString("N")));
            var fromFile = BoundedFile.ReadText(path,4096).Trim();
            if (fromFile.Length > 0)
            {
                apiKey = fromFile;
                reason = $"archivo {path}";
                return true;
            }
        }

        apiKey = null;
        reason =
            $"No se encontro la API key de NVIDIA.{Environment.NewLine}" +
            $"  1) Pegala en una sola linea en {SecretsDirName}/{ApiKeyFileName}" +
            (path is not null ? $" (el archivo existe pero esta vacio: {path})" : " (el archivo no existe)") +
            $"{Environment.NewLine}  2) O define la variable de entorno {ApiKeyEnvVar}";
        return false;
    }

    /// <summary>Resultado local sin lanzar por fallos esperados. El valor sensible nunca se registra.</summary>
    public static Result<ResolvedCredential, Failure> ResolveCredential()
    {
        var correlation = Guid.NewGuid().ToString("N");
        try
        {
            if (!TryGetLLMApiKey(out var key, out var source))
                return Result<ResolvedCredential, Failure>.Fail(new("credentials.missing", "Falta la credencial NVIDIA. Usa setup o NVIDIA_API_KEY.", correlation));
            if (key!.Length is < 16 or > 4096 || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
                return Result<ResolvedCredential, Failure>.Fail(new("credentials.malformed", "La credencial tiene formato local invalido. Usa setup.", correlation));
            return Result<ResolvedCredential, Failure>.Ok(new(key, source));
        }
        catch (Exception error) { return Result<ResolvedCredential, Failure>.Fail(Failures.FromException(error, correlation)); }
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

        var fallback=DefaultSecretsDirectory();
        return Directory.Exists(fallback) ? fallback : null;
    }

    /// <summary>Raiz Git del workspace, o CWD en distribuciones sin Git; independiente del almacen de secretos.</summary>
    public static string ResolveRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
                if (Directory.Exists(Path.Combine(dir.FullName,".git")) || File.Exists(Path.Combine(dir.FullName,".git"))) return dir.FullName;
        return Directory.GetCurrentDirectory();
    }

    public static string ResolveApplicationRoot()
    {
        for (var dir=new DirectoryInfo(AppContext.BaseDirectory);dir is not null;dir=dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName,".git")) || File.Exists(Path.Combine(dir.FullName,".git"))) return dir.FullName;
        return AppContext.BaseDirectory;
    }
    private static string DefaultSecretsDirectory()
    {
        var application=ResolveApplicationRoot();
        if (Directory.Exists(Path.Combine(application,".git")) || File.Exists(Path.Combine(application,".git"))) return Path.Combine(application,"local-secrets");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AgentLoop","local-secrets");
    }
    public static string ResolveSecretsDirectory() => FindSecretsDirectory() ?? DefaultSecretsDirectory();

    /// <summary>Enmascara una key para poder loguearla sin filtrarla.</summary>
    public static string Mask(string secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return "(vacio)";
        }

        return "[configurada; valor oculto]";
    }
}

/// <summary>Dato sensible solo para composicion; no serializar ni incluir en evidencia.</summary>
public sealed class ResolvedCredential(string value, string source)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Value { get; } = value;
    public string Source { get; } = source;
    public override string ToString() => "[credencial oculta]";
}
