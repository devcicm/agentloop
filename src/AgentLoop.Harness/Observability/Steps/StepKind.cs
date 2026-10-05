
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
    BusEvent,
}
