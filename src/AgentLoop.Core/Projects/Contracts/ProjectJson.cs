using System.Text.Json.Serialization;

namespace AgentLoop.Goals.Projects.Contracts;


public static class ProjectJson
{
    public static System.Text.Json.JsonSerializerOptions Options => new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32,
        RespectRequiredConstructorParameters = true,
    };
}
