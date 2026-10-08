using System.Text;
using AgentLoop.Harness.Functional;
using AgentLoop.Kernel.Projects;
using AgentLoop.Harness.Context;

namespace AgentLoop.Prompting;

public enum PromptValueType { Text, Integer, Boolean, Json }
public sealed record PromptVariableSpec(PromptValueType Type = PromptValueType.Text, string Source = "operator",
    bool Trusted = true, bool Sensitive = false, int MaxCharacters = 8192, bool AllowTruncation = false);
public sealed record PromptRenderOptions(int MaxCharacters = 48000, int MaxBytes = 192000);
public sealed record PromptVariableAudit(string Name,PromptValueType Type,string Source,bool Trusted);
public sealed record PromptRenderMetadata(int Characters, int Utf8Bytes, int EstimatedTokens, string Estimator, bool Exact,
    IReadOnlyList<PromptVariableAudit> Variables);
public sealed class PromptRenderResult(string text, PromptRenderMetadata metadata)
{
    public string Text { get; } = text;
    public PromptRenderMetadata Metadata { get; } = metadata;
    public override string ToString() => $"prompt({Metadata.Characters} chars)";
}
