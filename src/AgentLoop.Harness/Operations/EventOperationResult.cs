using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
namespace AgentLoop.Goals;

public sealed record EventOperationResult(OperationStatus Status, string Reason, Exception? Error = null)
{
    public bool IsSuccess => Status == OperationStatus.Succeeded;
}
