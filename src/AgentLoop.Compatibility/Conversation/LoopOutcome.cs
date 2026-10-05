namespace AgentLoop.Looping;


/// <summary>Outcome de un loop exitoso: por que se rompio la bandera y cuantas iteraciones llevo.</summary>
public sealed record LoopOutcome<TState>(LoopBreakReason Reason, int Iterations, TState FinalState) where TState : struct, Enum;
