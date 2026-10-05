using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AgentLoop.Goals.Projects;


/// <summary>Immutable admitted snapshot. DTO access returns a fresh copy, never authoritative mutable state.</summary>
public sealed class AdmittedProjectContract
{
    internal AdmittedProjectContract(string json, string root)
    {
        CanonicalJson = json;
        Root = root;
        ContractHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
    public string CanonicalJson { get; }
    public string ContractHash { get; }
    public string Root { get; }
    public ProjectGoalDto Contract => JsonSerializer.Deserialize<ProjectGoalDto>(CanonicalJson, ProjectJson.Options)!;
}
