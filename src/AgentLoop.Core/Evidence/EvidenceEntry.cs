using System.Security.Cryptography;
using System.Text;

namespace AgentLoop.Goals;


/// <summary>Una verificación de artefacto: qué se miró, cuánto pesaba y su hash.</summary>
public sealed record EvidenceEntry(string Path, long Bytes, string Hash, bool Ok, DateTimeOffset At);
