namespace AgentLoop.Harness.Lifecycle;


/// <summary>
/// Contrato de un sink de transiciones. Implementarlo no obliga a tocar
/// el resto del harness: cualquier consumidor (consola, archivo,
/// observabilidad) lo implementa y se suscribe.
/// </summary>
public interface ILifecycleObserver
{
    void OnLifecycle(LifecycleEvent transition);
}
