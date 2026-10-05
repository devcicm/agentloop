
namespace AgentLoop.Kernel.Projects;

public sealed record ProjectRuntimePolicy(int MaxActionsWithoutProgress, int MaxExplorationReads,
    int MaxArtifactBytes, int MaxEvidenceBytes, int MaxObservationChars, int MaxSnapshotObservationChars,
    int MaxDefectChars, int MaxNoteChars, int EvidencePageChars, int MaxCommandOutputChars,
    long MaxJournalBytes, int MaxJournalEntryBytes, int ContextMargin,
    int StopDrainSeconds);
