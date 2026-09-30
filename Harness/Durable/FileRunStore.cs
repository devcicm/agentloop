using System.Runtime.CompilerServices;
using System.Text.Json;
using AgentLoop.Harness.Core;

namespace AgentLoop.Harness.Durable;

/// <summary>
/// Almacena cada Snapshot como un JSON en artifacts/runs/&lt;runId&gt;.json.
/// La carpeta raiz se resuelve respecto a la raiz del repo (donde ya vive
/// artifacts/agentic-models.json). Se ignora el contenido si la version del
/// snapshot no coincide con la actual.
///
/// Single writer (agentico): cada run escribe solo su propio
/// &lt;runId&gt;.json y los RunId son Guid unicos por run, asi que dos runs
/// nunca compiten por el mismo archivo. Dentro de un proceso, un solo
/// GoalRunner escribe por run. No hay lock global porque no hay recurso
/// compartido que proteger; si manana dos procesos escribieran el mismo
/// directorio haria falta un lock por directorio (InstanceLock).
///
/// El listado es eager (recorre el directorio) porque se espera que el
/// numero de Runs por sesion sea bajo; si crece, se puede cambiar a un
/// indice o a SQLite sin tocar la interfaz.
/// </summary>
public sealed class FileRunStore : IRunStore
{
    private readonly string _root;
    private readonly JsonSerializerOptions _options;

    public FileRunStore(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("Debe especificarse un directorio raiz.", nameof(rootDirectory));
        }

        _root = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
        _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = false,
        };
    }

    public string Root => _root;

    public async Task SaveAsync(RunSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var path = PathFor(snapshot.RunId);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporary))
                await JsonSerializer.SerializeAsync(stream, snapshot, _options, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<RunSnapshot?> LoadAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(runId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var snapshot = await JsonSerializer.DeserializeAsync<RunSnapshot>(stream, _options, cancellationToken).ConfigureAwait(false);
            if (snapshot is null || snapshot.Version != RunSnapshot.CurrentVersion)
            {
                return null;
            }

            return snapshot;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async IAsyncEnumerable<Guid> ListAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
        {
            yield break;
        }

        foreach (var file in Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(file);
            if (Guid.TryParse(name, out var id))
            {
                yield return id;
            }

            await Task.Yield();
        }
    }

    private string PathFor(Guid runId) => Path.Combine(_root, $"{runId:N}.json");
}
