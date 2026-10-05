namespace AgentLoop.Goals;
/// <summary>Adaptadores instalados por el host para parseo, stream, recuperacion y persistencia de operaciones.</summary>
public sealed record OperationServices(
    Func<System.Text.Json.JsonElement, ParsedCompletion> ParseResponse,
    Func<string, Guid, long, int, IOperationEventJournal> CreateJournal,
    string JournalDirectory,
    Func<ICompletionStream> CreateStream,
    Func<Exception,bool> IsTransient,
    Action<string,ProjectSnapshotDto> SaveCheckpoint);
