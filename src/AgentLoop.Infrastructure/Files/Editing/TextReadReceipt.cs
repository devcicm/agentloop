namespace AgentLoop.Tools;
/// <summary>Lectura emitida por una instancia del registro: version y rango realmente devueltos.</summary>
internal sealed record TextReadReceipt(string Id, string Path, string Hash, int Start, int End);
