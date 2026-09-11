namespace AgentLoop.Harness.Functional;

/// <summary>
/// Marca una funcion como pura: su salida depende solo de su entrada y no
/// produce efectos observables (IO, reloj, red, estado mutable externo).
///
/// El atributo es documentacion + tooling: en tiempo de ejecucion el
/// <see cref="PureFunction{T,R}"/> puede comparar snapshots del bus y del
/// reloj antes y despues para detectar impurezas accidentales durante los
/// tests. No es un verificador formal: sirve para que las decisiones del
/// ledger, las reducciones del reactor y los filtros del bus queden
/// marcadas como contrato de pureza que los revisores pueden leer.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class PureAttribute : Attribute
{
    public string? Rationale { get; init; }

    public PureAttribute(string? rationale = null) => Rationale = rationale;
}

/// <summary>
/// Wrapper que ejecuta una funcion capturando el estado del bus y del
/// reloj antes y despues. Si la diferencia no es vacia, el wrapper puede
/// lanzar (en tests) o registrar (en produccion) la impureza detectada.
/// </summary>
public static class PureFunction
{
    public delegate bool Snapshot();

    /// <summary>
    /// Ejecuta <paramref name="work"/> con dos snapshots (antes/despues).
    /// Devuelve el resultado y emite una impureza si los snapshots
    /// cambiaron. Pensado para el SelfTest, no para el camino caliente.
    /// </summary>
    public static (T Result, bool Pure) Run<T>(Snapshot before, Snapshot after, Func<T> work)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(work);

        var okBefore = before();
        var result = work();
        var okAfter = after();
        var pure = okBefore && okAfter;
        return (result, pure);
    }
}
