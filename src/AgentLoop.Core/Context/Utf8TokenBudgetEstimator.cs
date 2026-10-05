using System.Text;
using System.Text.Json;
namespace AgentLoop.Harness.Context;


/// <summary>Conservative UTF-8 byte upper bound; explicitly not a tokenizer count.</summary>
public sealed class Utf8TokenBudgetEstimator : ITokenBudgetEstimator
{
    public string Version => "utf8-byte-upper-bound-v1";
    public bool Exact => false;
    public int Estimate(string text) => Encoding.UTF8.GetByteCount(text);
}
