using System.Text;

namespace AgentLoop.Cards;


/// <summary>Una jugada: la tarjeta que intento el agente y que dijo el sistema.</summary>
public sealed record CardPlay(
    string Name,
    StateCard? Card,
    IReadOnlyDictionary<string, string> Fields,
    bool Accepted,
    string? Rejection,
    int Depth,
    DateTimeOffset At)
{
    /// <summary>Vista como tipo suma: Ok si fue aceptada, Fail con el motivo si no.</summary>
    public Harness.Functional.Result<CardPlay, string> Result
        => Accepted
            ? Harness.Functional.Result<CardPlay, string>.Ok(this)
            : Harness.Functional.Result<CardPlay, string>.Fail(Rejection ?? "sin motivo");

    public string Summary
    {
        get
        {
            var first = Fields.Values.FirstOrDefault() ?? string.Empty;
            var clean = AgentLoop.Prompting.TextSanitizer.Plain(first);
            return clean.Length > 90 ? clean[..90] + "..." : clean;
        }
    }

    public override string ToString()
        => Accepted
            ? $"{Name} (nivel {Depth}) {Summary}"
            : $"{Name} RECHAZADA: {Rejection}";
}
