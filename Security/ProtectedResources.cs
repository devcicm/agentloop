namespace AgentLoop.Security;

public static class ProtectedResources
{
    public static bool IsProtected(string absolutePath)
    {
        var segments = absolutePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => OperatingSystem.IsWindows() ? s.TrimEnd(' ', '.') : s).ToArray();
        if (segments.Any(p => p.Equals("local-secrets", StringComparison.OrdinalIgnoreCase) || p.Equals(".git", StringComparison.OrdinalIgnoreCase))) return true;
        return segments.Any(p => p.Equals(".agentloop", StringComparison.OrdinalIgnoreCase))
            && (segments[^1].Equals("memory.json", StringComparison.OrdinalIgnoreCase) || segments[^1].Equals("security-policy.json", StringComparison.OrdinalIgnoreCase));
    }

    public static bool HasUnsafeWindowsAlias(string path)
    {
        if (!OperatingSystem.IsWindows()) return false;
        if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\")) return true;
        return path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment =>
            segment.EndsWith(' ') || segment.EndsWith('.')
            || segment.Contains(':') && !(segment.Length == 2 && char.IsAsciiLetter(segment[0]) && segment[1] == ':'));
    }
}
