namespace AgentLoop.Skills;

/// <summary>Procedimiento del operador. Sus herramientas son requisitos, nunca concesiones de permisos.</summary>
public sealed record SkillDefinition(string Id, string Version, string Purpose, IReadOnlyList<string> Intents,
    IReadOnlyList<string> RequiredTools, string Procedure, IReadOnlyList<string> Criteria, int MaxCalls = 8);

public interface ISkillCatalog
{
    IReadOnlyList<SkillDefinition> Find(string intent, IReadOnlySet<string> availableTools);
    IReadOnlyList<SkillDefinition> List(string intent);
}
