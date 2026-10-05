namespace AgentLoop.Harness.Lifecycle;


/// <summary>
/// Estados por los que pasa un Run durante su vida. Es la version explicita
/// de los RunStatus que el reactor ya emite implicitamente: cuando duerme
/// sobre el bus sin procesos vivos, esta en Paused; cuando se reactiva por
/// una senal, transiciona a Running. Hoy eso se perdia; con lifecycle
/// queda trazado en el Journey.
/// </summary>
public enum RunLifecycleState
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
