namespace AgentLoop.Terminals;
public sealed record CommandRiskAssessment(CommandRisk Risk, string Reason)
{
    public bool RequiresFreshApproval => Risk != CommandRisk.Standard;
}
