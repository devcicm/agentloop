
namespace AgentLoop.Harness.Functional;


/// <summary>
/// Tipo suma con dos casos: Ok(valor) o Fail(error). Es el reemplazo
/// del campo nullable "puede haber pasado algo mal" que contamina muchos
/// tipos del harness. Con un tipo suma el compilador obliga a PatternMatch,
/// asi "se me olvido comprobar el Failure" deja de compilar.
///
/// Diferencia con la version minimalista que venia en Composition.cs:
/// aqui cada caso lleva datos reales (no E=default en Ok), soporta
/// equality estructural completa, y expone helpers (Map, Bind, Match)
/// que la libreria estandar no da.
///
/// Convencion: T es el valor de exito, E es el motivo de fallo. Si el
/// fallo no aporta dato (tipo unit), usar <see cref="Unit"/>.
/// </summary>
public readonly record struct Result<T, E>
{
    public bool IsOk { get; }
    public T? Value { get; }
    public E? Error { get; }

    private Result(T? value, E? error, bool isOk)
    {
        Value = value;
        Error = error;
        IsOk = isOk;
    }

    public static Result<T, E> Ok(T value) => new(value, default, true);
    public static Result<T, E> Fail(E error) => new(default, error, false);

    /// <summary>Transforma el valor de exito conservando el error si lo hay.</summary>
    public Result<TOut, E> Map<TOut>(Func<T, TOut> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return IsOk ? Result<TOut, E>.Ok(step(Value!)) : Result<TOut, E>.Fail(Error!);
    }

    /// <summary>Encadena otra operacion que devuelve Result; si fallo, propaga.</summary>
    public Result<TOut, E> Bind<TOut>(Func<T, Result<TOut, E>> step)
    {
        ArgumentNullException.ThrowIfNull(step);
        return IsOk ? step(Value!) : Result<TOut, E>.Fail(Error!);
    }

    /// <summary>Aplica una de dos funciones segun el caso y devuelve su resultado.</summary>
    public TResult Match<TResult>(Func<T, TResult> onOk, Func<E, TResult> onFail)
    {
        ArgumentNullException.ThrowIfNull(onOk);
        ArgumentNullException.ThrowIfNull(onFail);
        return IsOk ? onOk(Value!) : onFail(Error!);
    }

    public override string ToString() => IsOk ? $"ok({Value})" : $"fail({Error})";
}
