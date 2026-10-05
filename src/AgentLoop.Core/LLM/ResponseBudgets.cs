
namespace AgentLoop.Provider;


public static class ResponseBudgets
{
    public static int DuringRecovery(int configured, int adaptive) => Math.Clamp(Math.Max(configured, adaptive), 64, 8192);

    public static int Recovery(int configured) => Math.Clamp((int)Math.Min((long)configured * 2, 8192), 64, 8192);
}
