using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Security.Contracts;


public sealed record DataFindingDto(string Code, string Source);
