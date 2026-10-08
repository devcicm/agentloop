using AgentLoop.Harness.Functional;

namespace AgentLoop.Operations;

public sealed record BudgetReservation(Guid Id, int InputUnits, int OutputUnits, decimal? EstimatedCost);
public sealed record BudgetSnapshot(long ChargedUnits, decimal? Cost, bool Estimated, int Calls, int Pending, string Estimator);

/// <summary>Reservas por llamada; razonamiento se contabiliza dentro de completion, nunca dos veces.</summary>
public sealed class InferenceBudgetLedger(HarnessPolicy policy, string estimator)
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, BudgetReservation> _pending = [];
    private long _charged;
    private decimal? _cost = policy.InputCostPerMillion is not null && policy.OutputCostPerMillion is not null ? 0m : null;
    private bool _estimated;
    private int _calls;
    public BudgetSnapshot Snapshot { get { lock (_gate) return new(_charged,_cost,_estimated,_calls,_pending.Count,estimator); } }

    public Result<BudgetReservation, Failure> Reserve(int input, int output, string correlation)
    {
        lock (_gate)
        {
        if (input < 0 || output < 0) throw new ArgumentOutOfRangeException(nameof(input));
        var units = (long)input + output;
        var cost = Price(input, output);
        if (units + _charged + _pending.Values.Sum(r => (long)r.InputUnits + r.OutputUnits) > policy.MaxTotalTokenUnits
            || policy.MaxCost is { } maximum && (_cost ?? 0) + (cost ?? 0) + _pending.Values.Sum(r => r.EstimatedCost ?? 0) > maximum)
            return Result<BudgetReservation, Failure>.Fail(new("budget.exhausted", "Presupuesto de inferencia agotado.", correlation));
        var reservation = new BudgetReservation(Guid.NewGuid(), input, output, cost);
        _pending.Add(reservation.Id, reservation); _calls++;
        return Result<BudgetReservation, Failure>.Ok(reservation);
        }
    }

    public void Settle(Guid id, long? input = null, long? output = null)
    {
        lock (_gate)
        {
        if (!_pending.Remove(id, out var reservation)) return;
        var actual = input is >= 0 && output is >= 0;
        var prompt = actual ? input!.Value : reservation.InputUnits;
        var completion = actual ? output!.Value : reservation.OutputUnits;
        _charged = checked(_charged + prompt + completion);
        if (_cost is not null) _cost += Price(prompt, completion);
        _estimated |= !actual; // Transporte incierto puede haber consumido recursos: no cobrar cero ficticio.
        }
    }

    private decimal? Price(long input, long output) => policy.InputCostPerMillion is { } a && policy.OutputCostPerMillion is { } b
        ? (input * a + output * b) / 1000000m : null;
}
