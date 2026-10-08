using AgentLoop.Harness.Functional;

namespace AgentLoop.Events;

/// <summary>Despacho serial disparado por publicaciones; controles prioritarios y cola finita, sin latido.</summary>
internal sealed class EventMailbox(int maximum, Func<AgentEvent,Task> handler, Action<Exception> failure)
{
    private readonly object _gate = new();
    private readonly Queue<AgentEvent> _controls = new(), _work = new();
    private bool _active;
    public long CoalescedOutput { get; private set; }
    public void Post(AgentEvent item)
    {
        lock (_gate)
        {
            var count = _controls.Count + _work.Count;
            if (item.Kind == AgentEventKind.ProcesoSalida && count >= maximum / 2)
            { CoalescedOutput++; return; } // La salida real permanece en buffers/journal de procesos.
            if (count >= maximum) throw new FailureException(new("events.capacity","Mailbox fuera de presupuesto.",item.RunId.ToString("N")));
            (IsControl(item.Kind) ? _controls : _work).Enqueue(item);
            if (!_active) { _active=true; Dispatch(); }
        }
    }
    private void Dispatch() => _ = Task.Run(async () =>
    {
        AgentEvent item;
        lock (_gate)
        {
            if (!_controls.TryDequeue(out item!) && !_work.TryDequeue(out item!)) { _active=false; return; }
        }
        try { await handler(item).ConfigureAwait(false); }
        catch (Exception error) { failure(error); }
        finally
        {
            lock (_gate) { if (_controls.Count + _work.Count > 0) Dispatch(); else _active=false; }
        }
    });
    private static bool IsControl(AgentEventKind kind) => kind is AgentEventKind.Cancelacion or AgentEventKind.Pausa
        or AgentEventKind.Reanudacion or AgentEventKind.Aprobacion or AgentEventKind.DeadlineAlcanzado
        or AgentEventKind.DetencionAgotada or AgentEventKind.PermisosRevocados;
}
