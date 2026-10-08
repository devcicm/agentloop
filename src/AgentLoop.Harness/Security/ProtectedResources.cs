
namespace AgentLoop.Security;


public static class ProtectedResources
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> TrustedFiles = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    public static void ProtectFile(string path) => TrustedFiles.TryAdd(Path.GetFullPath(path),0);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> TrustedDirectories = new();
    public static void ProtectDirectory(string directory) => TrustedDirectories.TryAdd(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,0);
    public static bool IsProtected(string absolutePath)
    {
        var full = Path.GetFullPath(absolutePath);
        if (TrustedFiles.ContainsKey(full)) return true;
        if (TrustedDirectories.Keys.Any(root => full.StartsWith(root,OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            || full.Equals(root.TrimEnd(Path.DirectorySeparatorChar),OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))) return true;
        var segments = absolutePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => OperatingSystem.IsWindows() ? s.TrimEnd(' ', '.') : s).ToArray();
        if (segments.Any(p => p.Equals("local-secrets", StringComparison.OrdinalIgnoreCase) || p.Equals(".git", StringComparison.OrdinalIgnoreCase) || p.Equals("edit-recovery", StringComparison.OrdinalIgnoreCase))) return true;
        return segments.Any(p => p.Equals(".agentloop", StringComparison.OrdinalIgnoreCase))
            && (segments[^1].Equals("memory.json", StringComparison.OrdinalIgnoreCase) || segments[^1].Equals("session.json", StringComparison.OrdinalIgnoreCase) || segments[^1].Equals("security-policy.json", StringComparison.OrdinalIgnoreCase) || segments[^1].Equals("harness-policy.json", StringComparison.OrdinalIgnoreCase));
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
