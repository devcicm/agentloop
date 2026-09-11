namespace AgentLoop.Harness.Steps;

/// <summary>Categorias de Step que el harness reconoce.</summary>
public enum StepKind
{
    ModelCall,
    ToolCall,
    Wait,
    Transition,
    Pattern,
    Error,
    Decision,
}

/// <summary>
/// Unidad atomica de trabajo del harness. Cada vez que el reactor invoca al
/// modelo, despacha una tool, transiciona de estado, espera un evento o
/// detecta un error, se registra un Step con su Correlation para que el
/// Journey lo muestre en orden.
///
/// Se modela como jerarquia: Step base + subtipos concretos que aportan el
/// Data especifico. Asi un ToolCall lleva el nombre y los argumentos; un
/// ModelCall lleva el conteo de tokens; etc.
/// </summary>
public abstract record Step(
    long Sequence,
    DateTimeOffset At,
    string Name,
    TimeSpan Elapsed,
    System.Collections.Generic.IReadOnlyDictionary<string, object?> Data)
{
    public abstract StepKind Kind { get; }
}

public sealed record ModelStep(
    long Sequence,
    DateTimeOffset At,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    TimeSpan Elapsed,
    string? Thinking) : Step(Sequence, At, $"model:{Model}", Elapsed,
        new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["prompt_tokens"] = PromptTokens,
            ["completion_tokens"] = CompletionTokens,
            ["thinking"] = Thinking,
        })
{
    public override StepKind Kind => StepKind.ModelCall;
}

public sealed record ToolStep(
    long Sequence,
    DateTimeOffset At,
    string Tool,
    string Arguments,
    string Output,
    bool Succeeded,
    TimeSpan Elapsed) : Step(Sequence, At, $"tool:{Tool}", Elapsed,
        new Dictionary<string, object?>
        {
            ["tool"] = Tool,
            ["arguments"] = Arguments,
            ["output"] = Output,
            ["succeeded"] = Succeeded,
        })
{
    public override StepKind Kind => StepKind.ToolCall;
}

public sealed record WaitStep(
    long Sequence,
    DateTimeOffset At,
    string Reason,
    TimeSpan Slept,
    bool Woken) : Step(Sequence, At, "wait", Slept,
        new Dictionary<string, object?>
        {
            ["reason"] = Reason,
            ["slept_ms"] = (long)Slept.TotalMilliseconds,
            ["woken"] = Woken,
        })
{
    public override StepKind Kind => StepKind.Wait;
}

public sealed record TransitionStep(
    long Sequence,
    DateTimeOffset At,
    string From,
    string To,
    string? Reason) : Step(Sequence, At, "transition", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["from"] = From,
            ["to"] = To,
            ["reason"] = Reason,
        })
{
    public override StepKind Kind => StepKind.Transition;
}

public sealed record PatternStep(
    long Sequence,
    DateTimeOffset At,
    string Pattern,
    string Decision,
    string? Reason) : Step(Sequence, At, $"pattern:{Pattern}", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["pattern"] = Pattern,
            ["decision"] = Decision,
            ["reason"] = Reason,
        })
{
    public override StepKind Kind => StepKind.Pattern;
}

public sealed record ErrorStep(
    long Sequence,
    DateTimeOffset At,
    string Where,
    string Message,
    bool Transient) : Step(Sequence, At, $"error:{Where}", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["where"] = Where,
            ["message"] = Message,
            ["transient"] = Transient,
        })
{
    public override StepKind Kind => StepKind.Error;
}

public sealed record DecisionStep(
    long Sequence,
    DateTimeOffset At,
    string Question,
    string Choice,
    string? Rationale) : Step(Sequence, At, "decision", TimeSpan.Zero,
        new Dictionary<string, object?>
        {
            ["question"] = Question,
            ["choice"] = Choice,
            ["rationale"] = Rationale,
        })
{
    public override StepKind Kind => StepKind.Decision;
}
