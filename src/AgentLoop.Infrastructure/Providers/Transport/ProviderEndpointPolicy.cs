namespace AgentLoop.Provider;

public static class ProviderEndpointPolicy
{
    public static Uri Validate(string endpoint, IReadOnlyList<string> allowedOrigins)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || !allowedOrigins.Any(origin => Uri.TryCreate(origin, UriKind.Absolute, out var allowed)
                && allowed.Scheme == uri.Scheme && allowed.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase) && allowed.Port == uri.Port))
            throw new FailureException(new("provider.endpoint_denied", "Endpoint no autorizado para recibir credenciales; requiere HTTPS y origen confiable.", Guid.NewGuid().ToString("N")));
        return uri;
    }
}
