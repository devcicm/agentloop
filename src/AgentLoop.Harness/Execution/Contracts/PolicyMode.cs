using System.Diagnostics;
using System.Text.RegularExpressions;

namespace AgentLoop.Terminals;


/// <summary>Como se tratan las ejecuciones antes de lanzarlas.</summary>
public enum PolicyMode
{
    /// <summary>Se ejecuta sin preguntar, salvo lo que caiga en la lista negra.</summary>
    Auto,

    /// <summary>Cada comando pasa por una confirmacion antes de arrancar.</summary>
    Confirmar,

    /// <summary>No se ejecuta nada: se responde que habria pasado. Util para revisar un plan.</summary>
    Simular,
}
