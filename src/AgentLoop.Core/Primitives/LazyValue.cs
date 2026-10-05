
namespace AgentLoop.Harness.Functional;


/// <summary>
/// Valor perezoso: la funcion que lo produce se evalua solo cuando alguien
/// pide <see cref="Force"/>. Pensado para casos donde el coste de calcular
/// un dato es alto y a veces no se necesita (por ejemplo, serializar un
/// Snapshot solo cuando el archivo no esta al dia).
///
/// No es thread-safe: si dos hilos llaman a Force a la vez, ambos ejecutan
/// la funcion. Para eso ya existe Lazy&lt;T&gt; del framework; este tipo es
/// mas ligero y explicito.
/// </summary>
public sealed class LazyValue<T>
{
    private readonly Func<T> _factory;
    private T? _value;
    private bool _evaluated;

    public LazyValue(Func<T> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public bool IsEvaluated => _evaluated;

    public T Force()
    {
        if (_evaluated)
        {
            return _value!;
        }

        _value = _factory();
        _evaluated = true;
        return _value;
    }

    public void Reset()
    {
        _value = default;
        _evaluated = false;
    }

    public static LazyValue<T> FromValue(T value) => new(() => value);
}
