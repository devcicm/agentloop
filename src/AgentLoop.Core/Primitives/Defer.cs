
namespace AgentLoop.Harness.Functional;


/// <summary>
/// Aplaza la ejecucion de una funcion hasta que el resultado se pida.
/// Equivalente a envolver en un LazyValue pero sin estado.
/// </summary>
public static class Defer
{
    public static LazyValue<T> Value<T>(Func<T> factory) => new(factory);
}
