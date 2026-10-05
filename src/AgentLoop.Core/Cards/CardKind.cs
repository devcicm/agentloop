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
