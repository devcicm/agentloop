
namespace AgentLoop.Kernel.Projects;


public sealed record ProjectRecoveryPolicy(long MaxJournalBytes, int MaxEntryBytes);
