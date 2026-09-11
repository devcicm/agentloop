using AgentLoop.Goals;

namespace AgentLoop.Harness.Core;

/// <summary>
/// Una operacion del agente como objeto de primera clase. Es la unidad
/// que el Runner ejecuta, el Journey narra, el RunStore persiste y los
/// observers observan. No se reusa entre Runs: cada intento es un Run
/// nuevo, identificado por su Guid.
/// </summary>
public sealed record Run(
    Guid Id,
    string Objective,
    Goal Goal,
    TimeSpan Budget,
    DateTimeOffset CreatedAt,
    string? Model = null)
{
    public static Run FromObjective(string objective, int deadlineSeconds, string? model = null)
    {
        var goal = Goal.Of(objective, deadlineSeconds);
        return new Run(
            Guid.NewGuid(),
            objective,
            goal,
            goal.Budget,
            DateTimeOffset.Now,
            model);
    }

    public Run WithModel(string model)
        => this with { Model = model };

    public Run WithBudget(TimeSpan budget)
        => this with { Budget = budget, Goal = Goal with { Budget = budget } };
}
