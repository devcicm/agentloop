using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AgentLoop.Goals;

public sealed record ObservationReceipt(string Id, string TransactionId, string Tool, string OutputHash, DateTimeOffset At);
