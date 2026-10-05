
namespace AgentLoop.Provider;


public static class TransientErrorClassifier
{
    public static bool IsRetryableStatus(int status) => status == 429 || status >= 500;
}
