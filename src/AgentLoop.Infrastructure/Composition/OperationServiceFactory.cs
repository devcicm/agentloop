namespace AgentLoop.Harness.Composition;
public static class OperationServiceFactory
{
    public static OperationServices Create(string? journalDirectory = null) => new(
        CompletionParser.Parse, (directory, runId, maxBytes, maxEntry) => new OperationEventJournal(directory, runId, maxBytes, maxEntry),
        journalDirectory ?? Path.Combine(SecretStore.ResolveRepositoryRoot(), ".agentloop", "event-journal"),
        () => new StreamAccumulator(),
        error => error is HttpRequestException or TimeoutException || error is LLMException provider && ((int)provider.StatusCode == 0 || TransientErrorClassifier.IsRetryableStatus((int)provider.StatusCode)),
        ProjectRecovery.SaveCheckpoint);
}
