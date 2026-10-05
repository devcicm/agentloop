using System.Text.Json;
namespace AgentLoop.StateMachines;


public enum AgentState
{
    Idle,
    Requesting,
    Parsing,
    ExecutingTools,
    Responding,
    Completed,
    Failed,
}
