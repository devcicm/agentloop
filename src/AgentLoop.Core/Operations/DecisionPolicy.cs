namespace AgentLoop.Operations;

public enum OperationIntent { General, Explain, Review, Edit, Build, Diagnose }
public enum DecisionKind { Wait, Infer, Reconcile, Close, Stop }
public sealed record OperationIntentState(OperationIntent Intent, string Objective, IReadOnlyList<string> Constraints);
public sealed record DecisionSnapshot(bool Terminal, bool Paused, bool Busy, bool HasFacts, bool Uncertain, bool GoalMet);

/// <summary>Precondiciones deterministas; el LLM propone alternativas solo si el host necesita una decision.</summary>
public static class DecisionPolicy
{
    public static DecisionKind Decide(DecisionSnapshot state) => state.Terminal ? DecisionKind.Stop
        : state.Uncertain ? DecisionKind.Reconcile
        : state.Paused || state.Busy ? DecisionKind.Wait
        : state.GoalMet ? DecisionKind.Close
        : state.HasFacts ? DecisionKind.Infer : DecisionKind.Wait;
}
