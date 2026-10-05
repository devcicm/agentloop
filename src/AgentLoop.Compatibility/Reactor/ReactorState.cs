using System.Text;
using System.Text.Json;
using System.Diagnostics;
namespace AgentLoop.Goals;


/// <summary>Estados del reactor. No son turnos: son fases de una reaccion a lo que pasa.</summary>
public enum ReactorState
{
    Arranque,
    Esperando,
    Razonando,
    Despachando,
    Cumplido,
    Fallido,
}
