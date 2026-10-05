using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Security.Contracts;


public sealed record PromptDataDto(int Version, string Source, string Trust, string Text);
