using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace AgentLoop.Terminals;


/// <summary>
/// Un patron vigilado sobre la salida de los procesos. Cuando aparece, se abre su latch y
/// se publica un evento: es la forma de que el agente diga "avisame cuando salga X" sin
/// tener que quedarse leyendo.
/// </summary>
public sealed class OutputWatcher : IOutputWatcher
{
    internal OutputWatcher(string id, string pattern, string? processId)
    {
        Id = id;
        Pattern = pattern;
        ProcessId = processId;
        Regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    }

    public string Id { get; }

    public string Pattern { get; }

    /// <summary>Proceso concreto que vigila, o null para vigilarlos todos.</summary>
    public string? ProcessId { get; }

    public Regex Regex { get; }

    public AsyncManualResetEvent Hit { get; } = new();

    public string? MatchedLine { get; private set; }

    public string? MatchedProcess { get; private set; }

    public bool Fired => Hit.IsSet;

    internal bool TryMatch(string processId, OutputLine line)
    {
        if (Fired || (ProcessId is not null && !string.Equals(ProcessId, processId, StringComparison.Ordinal)))
        {
            return false;
        }

        bool matched;
        try { matched = Regex.IsMatch(line.Text); }
        catch (RegexMatchTimeoutException) { return false; }
        if (!matched)
        {
            return false;
        }

        MatchedLine = line.Text;
        MatchedProcess = processId;
        Hit.Set();
        return true;
    }

    public override string ToString()
        => Fired
            ? $"{Id} /{Pattern}/ cumplido en {MatchedProcess}: {MatchedLine}"
            : $"{Id} /{Pattern}/ vigilando {ProcessId ?? "todo"}";
}
