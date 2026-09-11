namespace AgentLoop.Harness.Functional;

/// <summary>
/// Una funcion pura que se puede componer con otras: dada una entrada,
/// produce una salida. Es el primer pilar de la programacion funcional
/// expresado como tipo: las funciones son valores que se pueden pasar,
/// guardar y combinar.
///
/// No es obligatorio: una funcion anonima o un delegado valen. Pero si
/// quieres que el codigo explicite "esto se compone con aquello", la
/// interfaz lo deja claro en la firma.
/// </summary>
public interface IComposable<TIn, TOut>
{
    TOut Apply(TIn input);
}

/// <summary>
/// Helpers de composicion. <c>Compose(f, g)</c> produce una funcion
/// equivalente a <c>x => f(g(x))</c>; <c>Then</c> encadena dos IComposable;
/// <c>Lift</c> eleva un valor a una funcion constante.
/// </summary>
public static class Composable
{
    public static Func<TIn, TOut> AsFunc<TIn, TOut>(this IComposable<TIn, TOut> self)
        => self.Apply;

    public static Func<TIn, TOut2> Compose<TIn, TOut, TOut2>(
        this Func<TOut, TOut2> outer,
        Func<TIn, TOut> inner)
        => x => outer(inner(x));

    public static IComposable<TIn, TOut2> Then<TIn, TOut, TOut2>(
        this IComposable<TIn, TOut> first,
        IComposable<TOut, TOut2> second)
        => new ChainedComposable<TIn, TOut, TOut2>(first, second);

    public static IComposable<TIn, TOut> Lift<TIn, TOut>(TOut value)
        => new ConstantComposable<TIn, TOut>(value);

    private sealed class ChainedComposable<TIn, TMid, TOut>(IComposable<TIn, TMid> first, IComposable<TMid, TOut> second) : IComposable<TIn, TOut>
    {
        public TOut Apply(TIn input) => second.Apply(first.Apply(input));
    }

    private sealed class ConstantComposable<TIn, TOut>(TOut value) : IComposable<TIn, TOut>
    {
        public TOut Apply(TIn input) => value;
    }
}
