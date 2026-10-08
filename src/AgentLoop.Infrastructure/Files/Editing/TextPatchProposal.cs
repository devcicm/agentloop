namespace AgentLoop.Tools;

/// <summary>Propuesta inmutable de la misma version leida; diff completo disponible por paginas.</summary>
internal sealed record TextPatchProposal(string Id, string Path, string BaseHash, string Signature, string Diff, DateTimeOffset Expires);
