using System.Security.Cryptography;
namespace AgentLoop.Goals.Projects;
public sealed class FileProjectResources : IProjectResources
{
    public void CheckResource(string path) => ProjectContractAdmission.CheckResource(path);
    public void CheckExecutionRoot(string path) => ProjectContractAdmission.CheckExecutionRoot(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool Exists(string path) => File.Exists(path);
    public long Length(string path) => new FileInfo(path).Length;
    public string ReadText(string path) => File.ReadAllText(path);
    public Task WriteTextAsync(string path, string text, CancellationToken token) => File.WriteAllTextAsync(path,text,token);
    public string HashFile(string path, long maximumBytes) { using var stream = File.OpenRead(path); if(stream.Length>maximumBytes) throw new IOException("Artefacto excede politica de bytes."); return Convert.ToHexString(SHA256.HashData(stream)); }
    public ProjectSnapshotDto LoadSnapshot(string journal, AdmittedProjectContract contract, ProjectRecoveryPolicy policy) => ProjectRecovery.Load(journal,contract,policy);
    public IReadOnlyList<Guid> PreviousRuns(string journal, AdmittedProjectContract contract, ProjectRecoveryPolicy policy) => ProjectRecovery.RunIds(journal,contract,policy);
    public string JournalHash(string journal, ProjectRecoveryPolicy policy) => ProjectRecovery.JournalHash(journal,policy);
}
