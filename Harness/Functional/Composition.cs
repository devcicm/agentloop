namespace AgentLoop.Harness.Functional;

/// <summary>
/// Combinadores clasicos de programacion funcional: Pipe encadena
/// funciones unarias en orden, Bind encadena funciones que devuelven
/// Result&lt;R, E&gt;, Tap ejecuta un side-effect observador sin alterar el
/// valor, y Kleisli permite componer funciones que devuelven Result.
/// </summary>
public static class Composition
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
    /// Bind sobre Result: si la entrada es Success, ejecuta f; si es Failure,
    /// propaga el fallo.
    /// </summary>
    public static Result<TOut, TErr> Bind<TIn, TOut, TErr>(
        Result<TIn, TErr> input,
        Func<TIn, Result<TOut, TErr>> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return input.IsSuccess ? step(input.Value) : input.Cast<TOut>();
    }

    /// <summary>Compose para funciones que devuelven Result (Kleisli).</summary>
    public static Func<TIn, Result<TOut, TErr>> Kleisli<TIn, TMid, TOut, TErr>(
        Func<TIn, Result<TMid, TErr>> first,
        Func<TMid, Result<TOut, TErr>> second)
        => x => Bind(first(x), second);
}

/// <summary>
/// Tipo suma minimo con dos casos. Lo justo para que Bind y Kleisli
/// compilen sin tirar de librerias externas. Se reemplazara por el
/// refactor del commit H, que introduce un tipo mas rico.
/// </summary>
public readonly record struct Result<T, E>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public E Error { get; }

    private Result(T value, E error, bool isSuccess)
    {
        Value = value;
        Error = error;
        IsSuccess = isSuccess;
    }

    public static Result<T, E> Ok(T value) => new(value, default!, true);
    public static Result<T, E> Fail(E error) => new(default!, error, false);

    public Result<T2, E> Cast<T2>() => IsSuccess
        ? throw new InvalidOperationException("Cast sobre Success requiere un valor; usa Map.")
        : Result<T2, E>.Fail(Error);
}
