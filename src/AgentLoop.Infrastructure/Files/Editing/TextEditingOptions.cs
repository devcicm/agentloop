namespace AgentLoop.Tools;
/// <summary>Presupuestos del editor instalados por el host; independientes del proyecto o lenguaje.</summary>
public sealed record TextEditingOptions(int MaxFileBytes = 1000000, int MaxViewCharacters = 4096,
    int DefaultPageLines = 80, int MaxPageLines = 200, int MaxReadReceipts = 128,
    int MaxResponseCharacters = DataLimits.PromptOutputCharacters - 1024,
    bool RequirePatchForApply = true, bool RequireOldTextForLines = true, int MaxPatches = 32,
    int DiffPageCharacters = 4096, int PatchLifetimeSeconds = 600, int MaxPatchBytes = 8000000, long MaxRecoveryBytes = 64000000);
