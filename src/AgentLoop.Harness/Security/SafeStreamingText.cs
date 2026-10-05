using System.Text;

namespace AgentLoop.Security;


/// <summary>Buffer words so a recognizable key split across deltas cannot bypass redaction.</summary>
public sealed class SafeStreamingText
{
    private readonly StringBuilder _word = new();
    private bool _discard;
    private bool _bearer;
    public void Write(string chunk, Action<string> emit)
    {
        foreach (var c in chunk)
        {
            if (char.IsWhiteSpace(c)) { Flush(emit); emit(DataGuard.Display(c.ToString())); }
            else if (!_discard)
            {
                _word.Append(c);
                if (_word.Length > 4096) { _word.Clear(); _discard = true; emit("[texto largo omitido]"); }
            }
        }
    }
    public void Flush(Action<string> emit)
    {
        if (_word.Length > 0)
        {
            var word = _word.ToString();
            emit(_bearer ? "[SECRET REDACTED]" : DataGuard.Display(word));
            _bearer = word.Equals("Bearer", StringComparison.OrdinalIgnoreCase);
            _word.Clear();
        }
        _discard = false;
    }
}
