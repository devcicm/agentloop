
namespace AgentLoop.Harness.Patterns;


/// <summary>
/// Receta reutilizable que se aplica alrededor de una operacion. Una Pattern
/// recibe un <see cref="PatternContext"/> con el intento actual y devuelve la
/// decision: dejar pasar, esperar, reintentar o cortar.
///
/// Las Patterns no son middleware en el sentido de ASP.NET: cada una decide
/// sola en funcion del contexto, sin cadena fija. Composition las combina.
/// </summary>
public interface IPattern
{
    string Name { get; }

    PatternDecision Apply(PatternContext context);
}
