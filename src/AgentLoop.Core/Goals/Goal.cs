using System.Text;
using System.Text.Json;
namespace AgentLoop.Goals;


/// <summary>
/// Lo que se persigue y con que presupuesto. El objetivo no se da por cumplido por tiempo
/// ni por numero de pasos: solo por la tarjeta de cierre, que es lo unico que el sistema
/// acepta como final.
/// </summary>
public sealed record Goal(string Text, TimeSpan Budget)
{
    public static Goal Of(string text, int seconds = 300) => new(text.Trim(), TimeSpan.FromSeconds(seconds));
}
