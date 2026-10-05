namespace AgentLoop.Tools;
/// <summary>Presupuestos del editor instalados por el host; independientes del proyecto o lenguaje.</summary>
public sealed record TextEditingOptions(int MaxFileBytes = 1000000, int MaxViewCharacters = 4096,
    int DefaultPageLines = 80, int MaxPageLines = 200, int MaxReadReceipts = 128,
    int MaxResponseCharacters = DataLimits.PromptOutputCharacters - 1024);
