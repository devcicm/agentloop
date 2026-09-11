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
    public string Summary
    {
        get
        {
            var first = Fields.Values.FirstOrDefault() ?? string.Empty;
            return first.Length > 90 ? first[..90] + "..." : first;
        }
    }

    public override string ToString()
        => Accepted
            ? $"{Name} (nivel {Depth}) {Summary}"
            : $"{Name} RECHAZADA: {Rejection}";
}

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
        => $"{Id} nivel {Depth}: {Objective} ({Steps.Count} pasos, {(Verified ? "comprobada" : "sin comprobar")})";
}

/// <summary>
/// El libro de las tarjetas: valida cada jugada, lleva la pila de operaciones abiertas y
/// es quien decide si el objetivo esta cumplido.
///
/// Las reglas son transaccionales a proposito: no hay avance sin apertura, no hay cierre
/// sin comprobacion, y una apertura anidada obliga a cerrar de dentro hacia fuera. El
/// objetivo solo cuenta como cumplido cuando la pila queda vacia por un cierre, nunca por
/// agotarse el tiempo o los pasos.
/// </summary>
public sealed class CardLedger
{
    private readonly Stack<CardTransaction> _open = new();
    private readonly List<CardPlay> _plays = [];
    private readonly List<CardTransaction> _closed = [];
    private int _opened;

    public CardLedger(CardDeck deck) => Deck = deck;

    public CardDeck Deck { get; }

    /// <summary>El objetivo se cumplio: la transaccion raiz entrego su tarjeta de cierre.</summary>
    public bool GoalMet { get; private set; }

    /// <summary>La operacion se cerro como fallida desde la raiz.</summary>
    public bool Aborted { get; private set; }

    public string? Outcome { get; private set; }

    public bool Finished => GoalMet || Aborted;

    public int OpenCount => _open.Count;

    public CardTransaction? Current => _open.Count > 0 ? _open.Peek() : null;

    public IReadOnlyList<CardPlay> Plays => _plays;

    public IReadOnlyList<CardTransaction> ClosedTransactions => _closed;

    public int Rejected => _plays.Count(p => !p.Accepted);

    /// <summary>Juega una tarjeta. Nunca lanza: una jugada invalida vuelve rechazada con el motivo.</summary>
    public CardPlay Play(string name, IReadOnlyDictionary<string, string> fields)
    {
        var card = Deck.Find(name);

        if (card is null)
        {
            return Reject(name, null, fields, $"no existe la tarjeta {Quote(name)}. El mazo es: {string.Join(", ", Deck.Names)}");
        }

        var missing = card.RequiredFields
            .Where(f => !fields.TryGetValue(f, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (missing.Length > 0)
        {
            return Reject(name, card, fields, $"{Quote(card.Name)} exige {string.Join(", ", missing)} y vino sin eso.");
        }

        if (Finished)
        {
            return Reject(name, card, fields, "la operacion ya esta cerrada; no admite mas tarjetas.");
        }

        switch (card.Kind)
        {
            case CardKind.Apertura:
                return OpenTransaction(card, fields);

            case CardKind.Avance or CardKind.Comprobacion:
                return _open.Count == 0
                    ? Reject(name, card, fields, "no hay ninguna operacion abierta: juega la tarjeta de apertura antes de avanzar.")
                    : Accept(card, fields);

            case CardKind.Cierre:
                if (_open.Count == 0)
                {
                    return Reject(name, card, fields, "no hay ninguna operacion abierta que cerrar.");
                }

                if (!_open.Peek().Verified)
                {
                    return Reject(
                        name,
                        card,
                        fields,
                        "no se puede cerrar sin comprobar: juega la tarjeta de comprobacion con evidencia real antes del cierre.");
                }

                return CloseTransaction(card, fields, aborting: false);

            case CardKind.Aborto:
                return _open.Count == 0
                    ? Reject(name, card, fields, "no hay ninguna operacion abierta que abortar.")
                    : CloseTransaction(card, fields, aborting: true);

            default:
                return Reject(name, card, fields, $"tipo de tarjeta no soportado: {card.Kind}");
        }
    }

    /// <summary>Que tarjetas son legales en este momento. Va en el prompt de cada ciclo.</summary>
    public IReadOnlyList<string> AllowedNow()
    {
        if (Finished)
        {
            return [];
        }

        if (_open.Count == 0)
        {
            return Deck.Cards.Where(c => c.Kind == CardKind.Apertura).Select(c => c.Name).ToArray();
        }

        return Deck.Cards
            .Where(c => c.Kind != CardKind.Cierre || _open.Peek().Verified)
            .Select(c => c.Name)
            .ToArray();
    }

    /// <summary>La deuda pendiente: lo que falta para que la operacion cuente como terminada.</summary>
    public string? PendingDebt()
    {
        if (GoalMet)
        {
            return null;
        }

        if (_open.Count == 0)
        {
            return "no hay ninguna operacion abierta; el objetivo no se cumple hasta que abras una y la cierres.";
        }

        var current = _open.Peek();
        var falta = current.Verified
            ? "juega la tarjeta de cierre con resultado y evidencia"
            : "juega la tarjeta de comprobacion con evidencia real y despues la de cierre";

        return $"{_open.Count} operacion(es) sin cerrar; la de arriba es {Quote(current.Objective)}: {falta}.";
    }

    public string DescribeState()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"tarjetas jugadas: {_plays.Count} ({Rejected} rechazadas) | abiertas: {_open.Count} | cerradas: {_closed.Count}");

        foreach (var transaction in _open.Reverse())
        {
            builder.AppendLine($"  abierta {transaction}");
        }

        if (PendingDebt() is { } debt)
        {
            builder.AppendLine($"  pendiente: {debt}");
        }

        return builder.ToString().TrimEnd();
    }

    private CardPlay OpenTransaction(StateCard card, IReadOnlyDictionary<string, string> fields)
    {
        var transaction = new CardTransaction(
            $"t{++_opened}",
            fields["objetivo"],
            fields["plan"],
            _open.Count + 1);

        var play = Accept(card, fields, transaction.Depth, push: false);
        _open.Push(transaction);
        transaction.Record(play);
        return play;
    }

    private CardPlay CloseTransaction(StateCard card, IReadOnlyDictionary<string, string> fields, bool aborting)
    {
        var transaction = _open.Pop();
        var play = Accept(card, fields, transaction.Depth, push: false);
        transaction.Record(play);
        transaction.Close(aborting ? fields["motivo"] : fields["resultado"]);
        _closed.Add(transaction);

        // Solo el cierre de la raiz decide el destino de la operacion completa; las
        // sub-operaciones cierran su tramo y devuelven el control a la de fuera.
        if (_open.Count == 0)
        {
            Outcome = transaction.Outcome;
            if (aborting)
            {
                Aborted = true;
            }
            else
            {
                GoalMet = true;
            }
        }

        return play;
    }

    private CardPlay Accept(StateCard card, IReadOnlyDictionary<string, string> fields, int? depth = null, bool push = true)
    {
        var play = new CardPlay(card.Name, card, fields, true, null, depth ?? _open.Count, DateTimeOffset.Now);
        _plays.Add(play);

        if (push && _open.Count > 0)
        {
            _open.Peek().Record(play);
        }

        return play;
    }

    private CardPlay Reject(string name, StateCard? card, IReadOnlyDictionary<string, string> fields, string reason)
    {
        var play = new CardPlay(name, card, fields, false, reason, _open.Count, DateTimeOffset.Now);
        _plays.Add(play);
        return play;
    }

    private static string Quote(string value) => "'" + value + "'";
}
