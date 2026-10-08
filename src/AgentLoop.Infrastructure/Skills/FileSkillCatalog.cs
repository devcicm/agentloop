using System.Text.Json;
using AgentLoop.Skills;

namespace AgentLoop.Harness.Composition;

/// <summary>Solo carga procedimientos del directorio confiable elegido por el operador; nunca los descubre del prompt.</summary>
public sealed class FileSkillCatalog : ISkillCatalog
{
    private readonly IReadOnlyList<SkillDefinition> _skills;
    public FileSkillCatalog(string? directory, int maxCharacters)
    {
        if (directory is null) { _skills = []; return; }
        directory = Path.GetFullPath(directory);
        ValidateTrustedPath(directory);
        var skills = new List<SkillDefinition>();
        foreach (var file in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).Take(129))
        {
            if (skills.Count >= 128) throw new InvalidOperationException("Catalogo de skills excede presupuesto.");
            ValidateTrustedPath(file);
            if (new FileInfo(file).Length > maxCharacters * 4L) throw new InvalidOperationException("Skill fuera de presupuesto.");
            var text = BoundedFile.ReadText(file,checked(maxCharacters*4));
            if (DataGuard.Redact(text) != text) throw new InvalidOperationException("Skill contiene datos sensibles reconocibles.");
            var skill = JsonSerializer.Deserialize<SkillDefinition>(StrictJson.Parse(text, maxCharacters * 4).GetRawText(),
                new JsonSerializerOptions { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow });
            if (skill is null || string.IsNullOrWhiteSpace(skill.Id) || string.IsNullOrWhiteSpace(skill.Version)
                || string.IsNullOrWhiteSpace(skill.Purpose) || skill.Procedure is null || skill.Procedure.Length > maxCharacters
                || skill.Intents is null || skill.RequiredTools is null || skill.Criteria is null || skill.Criteria.Count == 0 || skill.MaxCalls < 1)
                throw new InvalidOperationException("Contrato de skill invalido.");
            skills.Add(skill);
        }
        if (skills.Select(s => s.Id).Distinct().Count() != skills.Count) throw new InvalidOperationException("IDs de skills duplicados.");
        _skills = skills;
        ProtectedResources.ProtectDirectory(directory);
    }
    private static void ValidateTrustedPath(string path)
    {
        if (ProtectedResources.HasUnsafeWindowsAlias(path) || path.Replace('\\','/').Split('/').Any(p=>p.Equals("local-secrets",StringComparison.OrdinalIgnoreCase)||p.Equals(".git",StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Directorio de skills inseguro.");
        for (var parent=new DirectoryInfo(Path.GetDirectoryName(path)!);parent is not null;parent=parent.Parent)
            if (parent.Exists && parent.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Skills no admiten enlaces.");
        if ((File.Exists(path)||Directory.Exists(path)) && File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException("Skill con enlace rechazada.");
    }
    public IReadOnlyList<SkillDefinition> Find(string intent, IReadOnlySet<string> availableTools)
        => _skills.Where(s => s.Intents.Contains(intent, StringComparer.OrdinalIgnoreCase) && s.RequiredTools.All(availableTools.Contains)).ToArray();
    public IReadOnlyList<SkillDefinition> List(string intent) => _skills.Where(s=>s.Intents.Contains(intent,StringComparer.OrdinalIgnoreCase)).ToArray();
}
