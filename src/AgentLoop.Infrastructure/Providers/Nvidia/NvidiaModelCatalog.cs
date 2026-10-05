
namespace AgentLoop.Provider;


public static class NvidiaModelCatalog
{
    public const string Glm53 = "z-ai/glm-5.3";
    public const string KimiK3 = "moonshotai/kimi-k3";
    public static IReadOnlyList<string> Models { get; } = [Glm53, KimiK3];
    public static bool IsGlm53(string id) => string.Equals(id, Glm53, StringComparison.OrdinalIgnoreCase);
}
