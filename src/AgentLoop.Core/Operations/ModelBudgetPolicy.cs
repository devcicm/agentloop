namespace AgentLoop.Operations;

/// <summary>Datos configurados por modelo; no incorpora tarifas ni ventanas supuestas del proveedor.</summary>
public sealed record ModelBudgetPolicy(int? ContextWindowUnits = null, decimal? InputCostPerMillion = null, decimal? OutputCostPerMillion = null);
