using System.Security.Cryptography;
using System.Text;

namespace AgentLoop.Goals;


/// <summary>Foto inmutable del manifiesto: archivos ordenados + hash agregado.</summary>
public sealed record EvidenceSnapshot(IReadOnlyList<EvidenceEntry> Files, string AggregateHash);
