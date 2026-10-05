namespace AgentLoop.Harness.Lifecycle;


/// <summary>
/// Wrapper que permite inyectar un callback antes de delegar al recorder real.
/// Pensado para tests que necesitan contar invocaciones concurrentes.
/// </summary>
internal sealed class HookedRecorder : ILifecycleObserver
{
    private readonly LifecycleRecorder _inner;
    private readonly Action _before;

    public HookedRecorder(LifecycleRecorder inner, Action before)
    {
        _inner = inner;
        _before = before;
    }

    public void OnLifecycle(LifecycleEvent transition)
    {
        _before();
        _inner.OnLifecycle(transition);
    }
}
