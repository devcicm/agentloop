using System.Text.Json;
namespace AgentLoop.Harness.Durable;


public sealed record JournalEntry(string Phase, AgentEvent Event, DateTimeOffset At, object? State = null);
