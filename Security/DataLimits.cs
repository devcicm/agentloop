namespace AgentLoop.Security;

public static class DataLimits
{
    public const int InputCharacters = 32000;
    public const int ToolArgumentsCharacters = 1100000;
    public const int ToolOutputCharacters = 1100000;
    public const int ProviderBytes = 2097152;
    public const int PromptOutputCharacters = 12000;
    public const int JsonDepth = 32;
    public const int ToolCallsPerResponse = 32;
}
