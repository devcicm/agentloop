using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentLoop.Security.Contracts;

public sealed record ToolDataMetadataDto(int Version, string Source, string Trust, string Sha256,
    bool Truncated, IReadOnlyList<DataFindingDto> Findings);
