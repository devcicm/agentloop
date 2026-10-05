
namespace AgentLoop.Kernel.Projects;


public interface ITokenBudgetEstimator
{
    string Version { get; }
    bool Exact { get; }
    int Estimate(string text);
}
