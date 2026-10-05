
namespace AgentLoop.Provider;


/// <summary>
/// Preferencia de razonamiento y presupuesto. Su representacion wire pertenece al adaptador del proveedor.
/// </summary>
public sealed class ThinkingOptions
{
    /// <summary>Apagado explicito: no es lo mismo que omitir el campo, y algunos backends lo distinguen.</summary>
    public static readonly ThinkingOptions Off = new() { Mode = ThinkingMode.Disabled };

    public ThinkingMode Mode { get; init; } = ThinkingMode.Enabled;


    /// <summary>Tope de tokens que el modelo puede gastar pensando antes de responder.</summary>

    public int? BudgetTokens { get; init; }

    public static ThinkingOptions Enabled(int budgetTokens)
    {
        if (budgetTokens < ThinkingPolicy.MinimumBudgetTokens)
        {
            throw new ArgumentOutOfRangeException(
                nameof(budgetTokens),
                budgetTokens,
                $"El presupuesto de thinking debe ser >= {ThinkingPolicy.MinimumBudgetTokens} tokens.");
        }

        return new ThinkingOptions { Mode = ThinkingMode.Enabled, BudgetTokens = budgetTokens };
    }

    public override string ToString()
        => Mode == ThinkingMode.Enabled ? $"enabled(budget={BudgetTokens?.ToString() ?? "-"})" : "disabled";
}
