using System.Text.RegularExpressions;
namespace AgentLoop.Terminals;
/// <summary>Heuristica configurable del host; no interpreta ni ejecuta codigo para clasificarlo.</summary>
public sealed class ShellCommandRiskClassifier : ICommandRiskClassifier
{
    private readonly Regex[] _destructive;
    private readonly Regex[] _opaque;
    public ShellCommandRiskClassifier(IEnumerable<string>? destructive = null, IEnumerable<string>? opaque = null)
    {
        _destructive = Compile(destructive ?? [
            @"\b(?:Remove-Item|Remove-ItemProperty|Clear-Content|Clear-Item|Clear-ItemProperty|Clear-RecycleBin|Remove-Partition|Stop-Process|Stop-Service|Remove-Service|Move-Item|Set-Content|Out-File|ri|mi|sc)\b",
            @"\b(?:File|Directory)\s*\]\s*::\s*(?:Delete|Move|WriteAllText|WriteAllBytes)|(?:^|[^>])>{1,2}(?!>)",
            @"(?:^|[;|&\s])(?:rm|rmdir|del|erase|rd|truncate|kill|killall|pkill|shred|unlink)(?:[;|&\s]|$)",
            @"\bgit\s+(?:reset\b[^\r\n]*--hard|clean\b|restore\b|checkout\b[^\r\n]*\s--\s|branch\b[^\r\n]*-[dD]\b|push\b[^\r\n]*(?:--force|\s-f\b))",
            @"\b(?:DROP\s+(?:TABLE|DATABASE)|TRUNCATE\s+TABLE|DELETE\s+FROM)\b",
            @"\b(?:robocopy\b[^\r\n]*/(?:MIR|PURGE)|rsync\b[^\r\n]*--delete)\b"
        ]);
        _opaque = Compile(opaque ?? [
            @"-(?:EncodedCommand|enc|ec)\b|\b(?:Invoke-Expression|iex|eval|exec)\b",
            @"\b(?:python(?:3)?|node|ruby|perl|php)\b[^\r\n]*\s-[ce]\b",
            @"\b(?:powershell(?:\.exe)?|pwsh|bash|sh|cmd(?:\.exe)?)\b",
            @"(?:\.|&|\bcall)\s*[^\r\n]*\.(?:ps1|bat|cmd|sh)\b|\b(?:python(?:3)?|node|ruby|perl|php)\b[^\r\n]*\.(?:py|js|mjs|rb|pl|php)\b",
            @"\b(?:curl|wget|Invoke-WebRequest|iwr)\b[^\r\n]*\|"
        ]);
    }
    private static Regex[] Compile(IEnumerable<string> patterns) => patterns.Select(p =>
        new Regex(p,RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))).ToArray();
    public CommandRiskAssessment Assess(ShellCommand command)
    {
        if (_destructive.Any(p => p.IsMatch(command.Command))) return new(CommandRisk.Destructive,"El comando incluye una operacion de borrado, perdida de cambios o terminacion.");
        if (_opaque.Any(p => p.IsMatch(command.Command))) return new(CommandRisk.Opaque,"Codigo anidado, dinamico o codificado: requiere aprobacion puntual.");
        return new(CommandRisk.Standard,"Sin indicadores destructivos en la clasificacion del host.");
    }
}
