using System.Text.Json;
namespace AgentLoop.Harness.Durable;


/// <summary>Registro incremental antes de admitir efectos y despues de aplicar eventos.</summary>
public sealed class OperationEventJournal : IOperationEventJournal
{
    private readonly FileStream _stream;
    private readonly long _maximumBytes;
    private readonly int _maximumEntryBytes;
    public string Path { get; }

    public OperationEventJournal(string directory, Guid runId, long maximumBytes = long.MaxValue, int maximumEntryBytes = int.MaxValue)
    {
        _maximumBytes = maximumBytes;
        _maximumEntryBytes = maximumEntryBytes;
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, runId.ToString("N") + ".jsonl");
        // Nunca sobrescribir un intento anterior: una recuperacion debe reconciliarlo.
        _stream = new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }

    public void Append(string phase, AgentEvent item, object? state = null)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(DataGuard.Redact(JsonSerializer.Serialize(new JournalEntry(phase, item, DateTimeOffset.UtcNow, state))) + "\n");
        if (bytes.Length > _maximumEntryBytes || _stream.Length + bytes.Length > _maximumBytes)
            throw new IOException("Presupuesto de journal agotado; conservar admisiones y reconciliar efectos pendientes.");
        _stream.Write(bytes);
        _stream.Flush(flushToDisk: true);
    }

    public static RecoveryInspection Inspect(string path)
    {
        var received = new HashSet<Guid>();
        var applied = new HashSet<Guid>();
        var pending = new HashSet<string>(StringComparer.Ordinal);
        var terminal = false;
        var runId = Guid.Empty;
        foreach (var line in File.ReadLines(path))
        {
            JournalEntry? entry;
            try { entry = JsonSerializer.Deserialize<JournalEntry>(line); }
            catch (JsonException) { break; } // Ultima escritura incompleta: conservar lo valido anterior.
            if (entry is null) continue;
            runId = entry.Event.RunId;
            if (entry.Phase == "received") received.Add(entry.Event.EventId);
            if (entry.Phase == "applied") applied.Add(entry.Event.EventId);
            if (entry.Phase == "terminal") terminal = true;
            if (entry.Event.Data is not { } data) continue;
            using var document = JsonDocument.Parse(data);
            if (!document.RootElement.TryGetProperty("action_id", out var action)) continue;
            var id = action.GetString()!;
            if (entry.Phase == "admitted") pending.Add(id);
            if (entry.Phase == "resolved") pending.Remove(id);
        }
        return new(runId, terminal, pending.ToArray(), received.Except(applied).Count());
    }

    public void Dispose() => _stream.Dispose();
}
