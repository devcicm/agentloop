namespace AgentLoop.Looping;


/// <summary>Motivo de fallo de un loop: termino con error o por una condicion no satisfecha.</summary>
public enum LoopFailureReason
{
    Errored,
    ConditionNotMet,
    Cancelled,
    Faulted,
}
