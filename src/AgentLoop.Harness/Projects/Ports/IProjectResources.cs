namespace AgentLoop.Goals.Projects;
/// <summary>Filesystem and recovery boundary installed by the host.</summary>
public interface IProjectResources
{
    void CheckResource(string path); void CheckExecutionRoot(string path); void CreateDirectory(string path);
    bool Exists(string path); long Length(string path); string ReadText(string path); string HashFile(string path, long maximumBytes);
    Task WriteTextAsync(string path, string text, CancellationToken token);
    ProjectSnapshotDto LoadSnapshot(string journal, AdmittedProjectContract contract, ProjectRecoveryPolicy policy);
    IReadOnlyList<Guid> PreviousRuns(string journal, AdmittedProjectContract contract, ProjectRecoveryPolicy policy);
    string JournalHash(string journal, ProjectRecoveryPolicy policy);
}
