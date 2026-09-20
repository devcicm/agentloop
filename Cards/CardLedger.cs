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

/// <summary>
/// El libro de las tarjetas: valida cada jugada, lleva la pila de operaciones abiertas y
/// es quien decide si el objetivo esta cumplido.
///
/// Las reglas son transaccionales a proposito: no hay avance sin apertura, no hay cierre
/// sin comprobacion, y una apertura anidada obliga a cerrar de dentro hacia fuera. El
/// objetivo solo cuenta como cumplido cuando la pila queda vacia por un cierre, nunca por
/// agotarse el tiempo o los pasos.
///
/// Además hay puerta de aceptación (agentico): al cerrar se verifica evidencia real
/// en código (archivos mencionados en el objetivo deben existir con contenido).
/// Un cf-report.md de 0 bytes no cierra nada en verde; el rechazo dice qué falta
/// (required_evidence) para que el reintento sepa qué le falta.
/// </summary>
public sealed class CardLedger
{
    private readonly Stack<CardTransaction> _open = new();
    private readonly List<CardPlay> _plays = [];
    private readonly List<CardTransaction> _closed = [];
    private readonly object _sync = new();
    private int _opened;

    public CardLedger(CardDeck deck) => Deck = deck;

    public CardDeck Deck { get; }

    /// <summary>
    /// Directorio contra el que se resuelven las rutas relativas mencionadas
    /// en los objetivos. Lo fija el composition root con el working directory
    /// de la política; por defecto el directorio actual (los tests usan temp).
    /// </summary>
    public string WorkingDirectory { get; set; } = Environment.CurrentDirectory;

    /// <summary>
    /// Manifiesto donde la puerta de aceptación registra cada verificación de
    /// artefacto. Vive en el ledger para que el gate y el reporte lean lo mismo.
    /// </summary>
    public AgentLoop.Goals.EvidenceManifest Evidence { get; } = new();

    /// <summary>El objetivo se cumplio: la transaccion raiz entrego su tarjeta de cierre.</summary>
    public bool GoalMet { get; private set; }

    /// <summary>La operacion se cerro como fallida desde la raiz.</summary>
    public bool Aborted { get; private set; }

    public string? Outcome { get; private set; }

    public bool Finished => GoalMet || Aborted;

    /// <summary>
    /// Toma el lock del ledger para que un read concurrente con Play no vea colecciones
    /// a medio mutar. Es un Monitor (reentrante), no un SemaphoreSlim: DescribeState
    /// llama a PendingDebt y Rejected por dentro, y con un semaforo eso se deadlockea
    /// en el mismo hilo. Ver commit del watchdog.
    /// </summary>
    private SyncScope ReadScope() => new(_sync);

    private sealed class SyncScope : IDisposable
    {
        private readonly object _sync;
        private bool _released;

        public SyncScope(object sync)
        {
            _sync = sync;
            Monitor.Enter(_sync);
        }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            Monitor.Exit(_sync);
        }
    }

    public int OpenCount
    {
        get
        {
            using var _ = ReadScope();
            return _open.Count;
        }
    }

    public CardTransaction? Current
    {
        get
        {
            using var _ = ReadScope();
            return _open.Count > 0 ? _open.Peek() : null;
        }
    }

    public IReadOnlyList<CardPlay> Plays
    {
        get
        {
            using var _ = ReadScope();
            return _plays.ToArray();
        }
    }

    public IReadOnlyList<CardTransaction> ClosedTransactions
    {
        get
        {
            using var _ = ReadScope();
            return _closed.ToArray();
        }
    }

    public int Rejected
    {
        get
        {
            using var _ = ReadScope();
            return _plays.Count(p => !p.Accepted);
        }
    }

    /// <summary>Juega una tarjeta. Nunca lanza: una jugada invalida vuelve rechazada con el motivo.</summary>
    public CardPlay Play(string name, IReadOnlyDictionary<string, string> fields)
    {
        using var _ = ReadScope();
        return PlayCore(name, fields);
    }

    private CardPlay PlayCore(string name, IReadOnlyDictionary<string, string> fields)
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

                // Puerta de aceptación (agentico): antes de aceptar el cierre se
                // comprueba evidencia real en código, no la palabra del modelo. Si
                // el objetivo menciona archivos, deben existir con contenido; si
                // falta algo, el rechazo dice exactamente qué (required_evidence)
                // para que el reintento sepa qué le falta.
                var faltante = RequiredEvidenceFor(_open.Peek());
                if (faltante.Count > 0)
                {
                    return Reject(
                        name,
                        card,
                        fields,
                        $"falta evidencia real para cerrar: {string.Join("; ", faltante)}. " +
                        "La tarjeta de cierre exige artefactos o salidas verificables, no solo texto.");
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
        using var _ = ReadScope();
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

    /// <summary>
    /// Evidencia que falta para poder cerrar la transacción abierta (vacío =
    /// puede cerrar). Es lo que el rechazo de cierre devuelve como
    /// required_evidence y lo que el reporte SIN CERRAR muestra. Solo mira
    /// archivos mencionados en el objetivo; sin menciones, mandan las reglas
    /// existentes del libro mayor.
    /// </summary>
    public IReadOnlyList<string> RequiredEvidence()
    {
        using var _ = ReadScope();
        if (Finished || _open.Count == 0)
        {
            return [];
        }

        return RequiredEvidenceFor(_open.Peek());
    }

    private IReadOnlyList<string> RequiredEvidenceFor(CardTransaction transaction)
    {
        var paths = AgentLoop.Goals.AcceptanceCriteria.DerivePaths(transaction.Objective);
        if (paths.Count == 0)
        {
            return [];
        }

        return AgentLoop.Goals.AcceptanceCriteria.MissingEvidence(paths, WorkingDirectory, Evidence);
    }

    /// <summary>La deuda pendiente: lo que falta para que la operacion cuente como terminada.</summary>
    public string? PendingDebt()
    {
        using var _ = ReadScope();
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
        using var _ = ReadScope();
        var builder = new StringBuilder();
        builder.AppendLine($"tarjetas jugadas: {_plays.Count} ({Rejected} rechazadas) | abiertas: {_open.Count} | cerradas: {_closed.Count}");

        foreach (var transaction in _open.Reverse())
        {
            builder.AppendLine($"  abierta {transaction}");
        }

        if (PendingDebt() is { } debt)
        {
            builder.AppendLine($"  pendiente: {AgentLoop.Prompting.TextSanitizer.Plain(debt)}");
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
