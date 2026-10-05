using System.Security.Cryptography;
using System.Text;

namespace AgentLoop.Goals;


/// <summary>
/// Manifiesto de evidencia del run, al estilo agentico (EvidenceLedger):
/// registra cada artefacto verificado con sus bytes y su sha256, y produce
/// un hash agregado determinista. La puerta de aceptación lo consulta al
/// cerrar; el journey lo persiste para auditoría.
///
/// A diferencia de creerle al campo "evidencia" que escribe el modelo, aquí
/// se mira el disco en el momento de verificar. Un cf-report.md de 0 bytes
/// no cierra nada en verde.
/// </summary>
public sealed class EvidenceManifest : IEvidenceManifest
{
    private readonly List<EvidenceEntry> _entries = [];
    private readonly object _sync = new();

    /// <summary>
    /// Verifica un archivo ahora mismo y registra el resultado. Nunca lanza
    /// por problemas de IO: un archivo ilegible queda como no-ok.
    /// </summary>
    public string ReadText(string path) { ProjectContractAdmission.CheckResource(path); return File.ReadAllText(path); }

    public EvidenceEntry Verify(string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);

        EvidenceEntry entry;
        try
        {
            if (!File.Exists(absolutePath))
            {
                entry = new EvidenceEntry(absolutePath, 0, string.Empty, false, DateTimeOffset.Now);
            }
            else
            {
                using var stream = File.OpenRead(absolutePath);
                var length = stream.Length;
                var hash = length == 0 ? string.Empty : Convert.ToHexString(SHA256.HashData(stream));
                entry = new EvidenceEntry(
                    absolutePath,
                    length,
                    hash,
                    length > 0,
                    DateTimeOffset.Now);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            entry = new EvidenceEntry(absolutePath, 0, string.Empty, false, DateTimeOffset.Now);
        }

        lock (_sync)
        {
            _entries.Add(entry);
        }

        return entry;
    }

    public IReadOnlyList<EvidenceEntry> Entries
    {
        get
        {
            lock (_sync)
            {
                return _entries.ToArray();
            }
        }
    }

    /// <summary>
    /// Foto ordenada por ruta + hash agregado (sha256 de "ruta:hash:bytes"
    /// unidos por "|", primeros 16 hex). Determinista: el orden de
    /// verificación no afecta al agregado.
    /// </summary>
    public EvidenceSnapshot Snapshot()
    {
        List<EvidenceEntry> files;
        lock (_sync)
        {
            files = _entries.OrderBy(e => e.Path, StringComparer.Ordinal).ToList();
        }

        var joined = string.Join("|", files.Select(e => $"{e.Path}:{e.Hash}:{e.Bytes}"));
        var aggregate = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
        return new EvidenceSnapshot(files, aggregate);
    }

}
