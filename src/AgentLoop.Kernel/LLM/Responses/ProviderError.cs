using System.Text.Json;

namespace AgentLoop.Parsing;


/// <summary>Error estructurado devuelto por el gateway.</summary>
public sealed record ProviderError(string Message, string Type, string Code);
