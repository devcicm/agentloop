namespace AgentLoop.Provider;

/// <summary>Cuota de solicitudes HTTP compartida entre adaptadores y reintentos del mismo objetivo.</summary>
public sealed class RequestAttemptBudget(int maximum)
{
    private int _attempts;
    private RequestAttemptBudget? _parent;
    private Func<bool>? _retry;
    public RequestAttemptBudget CreateScope(Func<bool> reserveRetry) => new(int.MaxValue) { _parent=this,_retry=reserveRetry };
    public int Attempts => Volatile.Read(ref _attempts);
    public bool Acquire()
    {
        if (_parent is not null)
        {
            if (!_parent.Acquire()) return false;
            if (Interlocked.Increment(ref _attempts) > 1 && !(_retry?.Invoke() ?? true)) return false;
            return true;
        }
        while (true) { var current = Attempts; if (current >= maximum) return false; if (Interlocked.CompareExchange(ref _attempts, current + 1, current) == current) return true; }
    }
    public static AsyncLocal<RequestAttemptBudget?> Current { get; } = new();
}
