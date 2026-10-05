
namespace AgentLoop.Harness.Functional;


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
