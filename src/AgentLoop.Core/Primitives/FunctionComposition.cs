
namespace AgentLoop.Harness.Functional;


/// <summary>
/// Combinadores clasicos de programacion funcional: Pipe encadena
/// funciones unarias en orden, Bind encadena funciones que devuelven
/// Result&lt;R, E&gt;, Tap ejecuta un side-effect observador sin alterar el
/// valor, y Kleisli permite componer funciones que devuelven Result.
/// </summary>
public static class FunctionComposition
{
    /// <summary>Pipe(x, f, g, h) = h(g(f(x))). El primer argumento es el valor inicial.</summary>
    public static TOut Pipe<TIn, TOut>(TIn value, params Func<TIn, TOut>[] stages)
    {
        ArgumentNullException.ThrowIfNull(stages);
        if (stages.Length == 0)
        {
            throw new ArgumentException("Al menos una etapa.", nameof(stages));
        }

        object current = value!;
        for (var i = 0; i < stages.Length; i++)
        {
            current = stages[i]((dynamic)current);
        }

        return (TOut)current;
    }

    /// <summary>Tap ejecuta <paramref name="side"/> y devuelve el valor original sin tocarlo.</summary>
    public static T Tap<T>(T value, Action<T> side)
    {
        ArgumentNullException.ThrowIfNull(side);
        side(value);
        return value;
    }

    /// <summary>
    /// Bind sobre Result: si la entrada es Ok, ejecuta step; si es Fail, propaga el error.
    /// </summary>
    public static Result<TOut, TErr> Bind<TIn, TOut, TErr>(
        Result<TIn, TErr> input,
        Func<TIn, Result<TOut, TErr>> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return input.IsOk ? step(input.Value!) : Result<TOut, TErr>.Fail(input.Error!);
    }

    /// <summary>Compose para funciones que devuelven Result (Kleisli).</summary>
    public static Func<TIn, Result<TOut, TErr>> Kleisli<TIn, TMid, TOut, TErr>(
        Func<TIn, Result<TMid, TErr>> first,
        Func<TMid, Result<TOut, TErr>> second)
        => x => Bind(first(x), second);
}
