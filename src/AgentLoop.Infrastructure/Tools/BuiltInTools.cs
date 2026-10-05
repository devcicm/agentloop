using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
public static class BuiltInTools
{

    /// <summary>Set por defecto para chat, run e interactive.</summary>
    public static ToolRegistry DefaultRegistry() => new([ArithmeticTools.AddNumbers, SystemTools.GetTime, FileTools.WriteTextFile, FileTools.ReadTextFile, FileTools.ListDirectory, SystemTools.GetSystemContext, SystemTools.WhereCommand, FileTools.FindFiles]);
}
