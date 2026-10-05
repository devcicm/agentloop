using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
namespace AgentLoop.Goals;


public enum OperationStatus { Waiting, Reasoning, Acting, Paused, Succeeded, Failed, Cancelled }
