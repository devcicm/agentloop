
namespace AgentLoop.Harness.Core;


/// <summary>Estado por el que pasa un Run durante su vida.</summary>
public enum RunStatus
{
    Created,
    Pending,
    Running,
    Paused,
    Suspending,
    Resuming,
    Succeeded,
    Failed,
    Cancelled,
    TimedOut,
    Checkpointed,
    ResumedFromCheckpoint,
}
