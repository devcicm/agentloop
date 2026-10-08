using AgentLoop.Operations;

namespace AgentLoop.Harness.Composition;

public static class HarnessPolicyLoader
{
    public static HarnessPolicy Load()
    {
        var configured=Environment.GetEnvironmentVariable("AGENTLOOP_HARNESS_POLICY");
        if (configured is not null && !Path.IsPathFullyQualified(configured)) throw new InvalidOperationException("La politica explicita requiere ruta absoluta.");
        var path = configured ?? Path.Combine(SecretStore.ResolveApplicationRoot(), ".agentloop", "harness-policy.json");
        ProtectedResources.ProtectFile(path);
        if (configured is not null && !File.Exists(path)) throw new InvalidOperationException("Politica explicita no encontrada.");
        for (var node = new FileInfo(path) as FileSystemInfo; node is not null; node = node is FileInfo f ? f.Directory : ((DirectoryInfo)node).Parent)
            if (node.Exists && node.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Politica con enlace rechazada.");
        var policy = File.Exists(path) ? System.Text.Json.JsonSerializer.Deserialize<HarnessPolicy>(
            StrictJson.Parse(BoundedFile.ReadText(path,64000), 64000).GetRawText(), new System.Text.Json.JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidOperationException("Politica vacia.") : new();
        if (policy.SkillsDirectory is not null) policy=policy with { SkillsDirectory=Path.GetFullPath(policy.SkillsDirectory,Path.GetDirectoryName(path)!) };
        policy.Validate(); return policy;
    }
}
