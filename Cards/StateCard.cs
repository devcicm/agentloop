using System.Text;

namespace AgentLoop.Cards;

/// <summary>Papel que cumple la tarjeta dentro de la transaccion.</summary>
public enum CardKind
{
    /// <summary>Abre una transaccion. Contrae la deuda de cerrarla.</summary>
    Apertura,

    /// <summary>Registra un paso dentro de la transaccion abierta.</summary>
    Avance,

    /// <summary>Verifica un resultado. Sin al menos una, no se puede cerrar.</summary>
    Comprobacion,

    /// <summary>Cierra la transaccion: es la tarjeta de finalizar (el commit).</summary>
    Cierre,

    /// <summary>Cierra la transaccion como fallida (el rollback).</summary>
    Aborto,
}

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

/// <summary>
/// El mazo que se le entrega al agente. Es el contrato de la operacion: estas son las
/// tarjetas que el sistema reconoce, y ninguna otra.
/// </summary>
public sealed class CardDeck
{
    private readonly Dictionary<string, StateCard> _cards;

    public CardDeck(IEnumerable<StateCard> cards)
    {
        Cards = cards.ToArray();
        _cards = Cards.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Mazo por defecto: una transaccion con apertura, avances, comprobacion obligatoria
    /// antes del cierre, y aborto explicito. Es deliberadamente corto: cuantas mas tarjetas,
    /// mas se equivoca el modelo eligiendo.
    /// </summary>
    public static CardDeck Transaccional { get; } = new(
    [
        new StateCard(
            "abrir",
            CardKind.Apertura,
            "Abre la operacion (o una sub-operacion) declarando que se va a conseguir y como. Toda apertura queda en deuda hasta que se cierre o se aborte.",
            ["objetivo", "plan"]),

        new StateCard(
            "avanzar",
            CardKind.Avance,
            "Registra un paso ya dado o el que vas a dar ahora, con lo que has observado.",
            ["paso"]),

        new StateCard(
            "comprobar",
            CardKind.Comprobacion,
            "Verifica un resultado contra algo real (salida de un comando, codigo de salida, contenido de un archivo). Hace falta al menos una para poder cerrar.",
            ["comprobacion", "resultado"]),

        new StateCard(
            "cerrar",
            CardKind.Cierre,
            "Da por terminada la operacion abierta. Es la tarjeta de finalizar: sin ella el objetivo no cuenta como cumplido.",
            ["resultado", "evidencia"]),

        new StateCard(
            "abortar",
            CardKind.Aborto,
            "Cierra la operacion abierta como fallida cuando no es alcanzable, explicando por que.",
            ["motivo"]),
    ]);

    public IReadOnlyList<StateCard> Cards { get; }

    /// <summary>Todos los campos que alguna tarjeta puede pedir, para el schema de la tool.</summary>
    public IReadOnlyList<string> AllFields
        => Cards.SelectMany(c => c.RequiredFields).Distinct(StringComparer.Ordinal).ToArray();

    public StateCard? Find(string name)
        => _cards.TryGetValue(name.Trim(), out var card) ? card : null;

    public StateCard Final => Cards.First(c => c.Kind == CardKind.Cierre);

    public IReadOnlyList<string> Names => Cards.Select(c => c.Name).ToArray();

    /// <summary>El mazo tal como se le explica al modelo en el prompt de sistema.</summary>
    public string Render()
    {
        var builder = new StringBuilder();
        foreach (var card in Cards)
        {
            builder.AppendLine($"  - {card.Describe()}");
        }

        return builder.ToString().TrimEnd();
    }
}
