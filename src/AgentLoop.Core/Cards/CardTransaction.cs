using System.Text;

namespace AgentLoop.Cards;


/// <summary>
/// Una operacion abierta. Existe desde su tarjeta de apertura y solo desaparece cuando
/// llega su cierre o su aborto: esa es la deuda que el sistema no deja olvidar.
/// </summary>
public sealed class CardTransaction
{
    public CardTransaction(string id, string objective, string plan, int depth)
    {
        Id = id;
        Objective = objective;
        Plan = plan;
        Depth = depth;
        OpenedAt = DateTimeOffset.Now;
    }

    public string Id { get; }

    public string Objective { get; }

    public string Plan { get; }

    public int Depth { get; }

    public DateTimeOffset OpenedAt { get; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public List<CardPlay> Steps { get; } = [];

    /// <summary>Se jugo al menos una comprobacion: requisito para cerrar.</summary>
    public bool Verified { get; private set; }

    public bool Closed => ClosedAt is not null;

    public string? Outcome { get; private set; }

    internal void Record(CardPlay play)
    {
        Steps.Add(play);
        if (play.Card?.Kind == CardKind.Comprobacion)
        {
            Verified = true;
        }
    }

    internal void Close(string outcome)
    {
        ClosedAt = DateTimeOffset.Now;
        Outcome = outcome;
    }

    public TimeSpan Elapsed => (ClosedAt ?? DateTimeOffset.Now) - OpenedAt;

    public override string ToString()
        => $"{Id} nivel {Depth}: {AgentLoop.Prompting.TextSanitizer.Plain(Objective)} ({Steps.Count} pasos, {(Verified ? "comprobada" : "sin comprobar")})";
}
