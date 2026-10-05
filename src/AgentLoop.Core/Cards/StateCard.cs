using System.Text;

namespace AgentLoop.Cards;


/// <summary>
/// Una tarjeta de estado: el vocabulario con el que el agente declara en que punto de la
/// operacion esta. No ejecuta nada; lo que hace es hacer explicito y verificable el avance,
/// para que el sistema pueda exigir que toda operacion abierta termine entregando su cierre.
/// </summary>
/// <param name="RequiredFields">Campos sin los cuales la jugada se rechaza.</param>
public sealed record StateCard(
    string Name,
    CardKind Kind,
    string Purpose,
    IReadOnlyList<string> RequiredFields)
{
    public string Describe()
    {
        var fields = RequiredFields.Count == 0
            ? "sin campos"
            : "exige " + string.Join(", ", RequiredFields);

        return $"{Name} ({Kind.ToString().ToLowerInvariant()}, {fields}): {Purpose}";
    }
}
