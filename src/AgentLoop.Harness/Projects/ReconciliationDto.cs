using System.Security.Cryptography;
using System.Text.Json;
namespace AgentLoop.Goals.Projects;

public sealed record ReconciliationDto(int Version, string SourceHash, string ContractHash, string ActionId, string Decision, string Note,
    IReadOnlyDictionary<string, string> ObservedHashes, DateTimeOffset At);
