using System.Diagnostics;
using System.Text;
namespace AgentLoop.Terminals;


/// <summary>Una linea de salida capturada, con su origen.</summary>
public sealed record OutputLine(int Index, DateTimeOffset At, bool IsError, string Text)
{
    public override string ToString() => IsError ? $"err| {Text}" : $"out| {Text}";
}
