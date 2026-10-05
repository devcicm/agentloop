using System.Text.Json;
namespace AgentLoop.Harness.Durable;

public sealed record RecoveryInspection(Guid RunId, bool Terminal, IReadOnlyList<string> UnresolvedActions, int UnconfirmedEvents);
