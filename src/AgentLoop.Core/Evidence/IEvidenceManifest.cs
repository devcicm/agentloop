namespace AgentLoop.Goals;
/// <summary>Host observations; a ledger never reads the filesystem directly.</summary>
public interface IEvidenceManifest
{
    EvidenceEntry Verify(string path);
    string ReadText(string path);
    IReadOnlyList<EvidenceEntry> Entries { get; }
    EvidenceSnapshot Snapshot();
}
