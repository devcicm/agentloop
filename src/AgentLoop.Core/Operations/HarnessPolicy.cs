namespace AgentLoop.Operations;

/// <summary>Limites y decisiones generales del host; no contiene reglas de negocio.</summary>
public sealed record HarnessPolicy
{
    public int MaxPendingEvents { get; init; } = 4096;
    public int MaxAppliedEvents { get; init; } = 100000;
    public int MaxPendingFacts { get; init; } = 512;
    public int MaxObserverBacklog { get; init; } = 512;
    public int MaxHttpAttempts { get; init; } = 80;
    public IReadOnlyList<string> AllowedProviderOrigins { get; init; } = [];
    public int MaxRepeatedActions { get; init; } = 3;
    public int MaxExplorationActions { get; init; } = 12;
    public int MaxActionsWithoutProgress { get; init; } = 24;
    public int MaxInputUnits { get; init; } = 96000;
    public int ContextMargin { get; init; } = 1024;
    public int ContextWindowUnits { get; init; } = 128000;
    public long MaxTotalTokenUnits { get; init; } = 2000000;
    public decimal? MaxCost { get; init; }
    public decimal? InputCostPerMillion { get; init; }
    public decimal? OutputCostPerMillion { get; init; }
    public long MaxJournalBytes { get; init; } = 64 * 1024 * 1024;
    public int MaxJournalEntryBytes { get; init; } = 2 * 1024 * 1024;
    public bool RequireExplicitCriteria { get; init; }
    public string? SkillsDirectory { get; init; }
    public IReadOnlyDictionary<string,ModelBudgetPolicy> ModelBudgets { get; init; } = new Dictionary<string,ModelBudgetPolicy>();
    public int MaxKnowledgeNodes { get; init; } = 4096;
    public int MaxGraphContextCharacters { get; init; } = 6000;
    public int MaxSkillCharacters { get; init; } = 8000;

    public void Validate()
    {
        if (MaxHttpAttempts < 1 || AllowedProviderOrigins is null || MaxPendingEvents < 1 || MaxAppliedEvents < 1 || MaxPendingFacts < 1 || MaxObserverBacklog < 1
            || MaxRepeatedActions < 1 || MaxExplorationActions < 0 || MaxActionsWithoutProgress < 1 || MaxInputUnits < 1 || ContextMargin < 0
            || ContextWindowUnits <= ContextMargin || MaxTotalTokenUnits < 1 || MaxJournalBytes < 1
            || MaxKnowledgeNodes < 1 || MaxGraphContextCharacters < 128 || MaxJournalEntryBytes < 1 || MaxJournalEntryBytes > MaxJournalBytes || MaxSkillCharacters < 1
            || MaxCost is < 0 || InputCostPerMillion is < 0 || OutputCostPerMillion is < 0)
            throw new ArgumentException("Politica del harness invalida.");
        if (MaxCost is not null && (InputCostPerMillion is null || OutputCostPerMillion is null))
            throw new ArgumentException("Un limite monetario requiere ambas tarifas configuradas.");
        if (ModelBudgets is null || ModelBudgets.Any(p=>string.IsNullOrWhiteSpace(p.Key)||p.Value is null||p.Value.ContextWindowUnits is <= 0||p.Value.InputCostPerMillion is < 0||p.Value.OutputCostPerMillion is < 0))
            throw new ArgumentException("Presupuestos por modelo invalidos.");
    }
    public HarnessPolicy ForModel(string model)
    {
        var result=ModelBudgets.TryGetValue(model,out var budget) ? this with { ContextWindowUnits=budget.ContextWindowUnits ?? ContextWindowUnits,
            InputCostPerMillion=budget.InputCostPerMillion ?? InputCostPerMillion,OutputCostPerMillion=budget.OutputCostPerMillion ?? OutputCostPerMillion } : this;
        result.Validate(); return result;
    }
}
