using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace AgentLoop.Tools;

/// <summary>Original durable bajo el almacen protegido; un fallo de escritura exige reconciliar, no reintentar ciegamente.</summary>
public static class EditRecoveryStore
{
    public static string Prepare(string path, byte[] original, string oldHash, string newHash, long maximumBytes)
    {
        var root = SecretStore.ResolveRepositoryRoot();
        var directory = Path.Combine(SecretStore.ResolveSecretsDirectory(), "edit-backups");
        for (var node = new DirectoryInfo(directory); node is not null; node = node.Parent)
            if (node.Exists && node.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Almacen de recuperacion con enlace rechazado.");
        Directory.CreateDirectory(directory);
        if (Directory.EnumerateFiles(directory,"*.bin").Sum(p=>new FileInfo(p).Length)+original.LongLength > maximumBytes)
            throw new InvalidOperationException("Almacen de recuperacion lleno; conserva/revisa respaldos con el operador antes de editar.");
        var id = Guid.NewGuid().ToString("N");
        var backup = Path.Combine(directory, id + ".bin");
        LocalCredentialWriter.CheckGitIsolation(backup);
        using (var file = new FileStream(backup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            if (OperatingSystem.IsWindows())
            {
                var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Usuario local no resuelto.");
                var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(user);
                acl.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow)); file.SetAccessControl(acl);
            }
            else File.SetUnixFileMode(backup, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            file.Write(original); file.Flush(true);
        }
        var journal = Path.Combine(root, ".agentloop", "edit-recovery", id + ".json");
        AtomicFileWriter.Write(journal, JsonSerializer.Serialize(new { version = 1, id, path, oldHash, newHash, backup, status = "prepared" }));
        return journal;
    }
    public static void Complete(string journal)
    {
        using var data = JsonDocument.Parse(BoundedFile.ReadText(journal,64000));
        var fields = data.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        fields["status"] = JsonSerializer.SerializeToElement("completed");
        AtomicFileWriter.Write(journal, JsonSerializer.Serialize(fields));
    }

    public static async Task RestoreAsync(string id,string expectedCurrentHash,CancellationToken token)
    {
        if (!Guid.TryParseExact(id,"N",out _)) throw new ArgumentException("Id de recuperacion invalido.");
        var journal=Path.Combine(SecretStore.ResolveRepositoryRoot(),".agentloop","edit-recovery",id+".json");
        using var data=JsonDocument.Parse(BoundedFile.ReadText(journal,64000));
        var path=data.RootElement.GetProperty("path").GetString()!; ProjectContractAdmission.CheckResource(path);
        var backup=Path.Combine(SecretStore.ResolveSecretsDirectory(),"edit-backups",id+".bin");
        await using var file=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None);
        if (file.Length>8000000 || new FileInfo(backup).Length>8000000) throw new InvalidOperationException("Recuperacion fuera de presupuesto.");
        var current=new byte[(int)file.Length]; await file.ReadExactlyAsync(current,token);
        var actual=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(current));
        if (!actual.Equals(expectedCurrentHash,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("El archivo cambio; vuelve a inspeccionar antes de restaurar.");
        for (var node = new FileInfo(backup) as FileSystemInfo; node is not null; node = node is FileInfo f ? f.Directory : ((DirectoryInfo)node).Parent)
            if (node.Exists && node.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Respaldo con enlace rechazado.");
        token.ThrowIfCancellationRequested();
        var original=BoundedFile.ReadBytes(backup,8000000);
        var oldHash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original));
        if (oldHash!=data.RootElement.GetProperty("oldHash").GetString()) throw new InvalidOperationException("Respaldo incompatible; no se restaurara.");
        var undo=Prepare(path,current,actual,oldHash,64000000);
        token.ThrowIfCancellationRequested();
        file.Position=0; await file.WriteAsync(original,token); file.SetLength(original.Length); await file.FlushAsync(token); file.Flush(true);
        Complete(undo);
    }
}
