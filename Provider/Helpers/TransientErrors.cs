namespace AgentLoop.Provider;

public static class TransientErrors
{
    public static bool IsRetryableStatus(int status) => status == 429 || status >= 500;
}
