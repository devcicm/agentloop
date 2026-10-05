namespace AgentLoop.Terminals;

/// <summary>Rechazo del supervisor antes de arrancar el proceso; no acredita efectos inciertos.</summary>
public sealed class ExecutionRejectedException(string message) : InvalidOperationException(message);
