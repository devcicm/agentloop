namespace AgentLoop.Goals;
/// <summary>Fail closed for file criteria when the host has installed no observer.</summary>
public sealed class UnavailableEvidenceManifest : IEvidenceManifest
{
    public EvidenceEntry Verify(string path) => new(path, 0, "", false, DateTimeOffset.UtcNow);
    public string ReadText(string path) => throw new InvalidOperationException("No artifact observer installed.");
    public IReadOnlyList<EvidenceEntry> Entries => [];
    public EvidenceSnapshot Snapshot() => new([], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData([]))[..16]);
}
