using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AgentLoop.Goals;


public sealed record ArtifactCriterion(string Path, string? Contains = null, bool ValidJson = false, bool MustChange = true);
