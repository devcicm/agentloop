using System.Security.Cryptography;
using System.Text.Json;
namespace AgentLoop.Goals.Projects;


public sealed record ProjectRecoveryInspection(string ContractHash, ProjectSnapshotDto Snapshot, IReadOnlyList<string> UncertainActions, bool Terminal);
