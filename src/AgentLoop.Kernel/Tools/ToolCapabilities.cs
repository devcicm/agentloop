namespace AgentLoop.Tools;

public enum ToolEffect { None, HostState, FileSystem, Process, Network, Unknown }
public sealed record ToolCapabilities(ToolEffect Effect = ToolEffect.Unknown, bool Cacheable = false, bool ProducesEvidence = false)
{
    public bool ReadOnly => Effect == ToolEffect.None;
    public static ToolCapabilities Observation { get; } = new(ToolEffect.None, ProducesEvidence: true);
}
