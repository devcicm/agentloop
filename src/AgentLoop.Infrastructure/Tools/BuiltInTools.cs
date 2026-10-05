using System.Globalization;
using System.Diagnostics;
using System.Text.Json;

namespace AgentLoop.Tools;
public static class BuiltInTools
{

    /// <summary>Set por defecto para chat, run e interactive.</summary>
    public static ToolRegistry DefaultRegistry(TextEditingOptions? editingOptions = null) =>
        new ToolRegistry([ArithmeticTools.AddNumbers, SystemTools.GetTime, FileTools.ListDirectory, SystemTools.GetSystemContext, SystemTools.WhereCommand, FileTools.FindFiles])
            .Combine(new TextEditingTools(editingOptions).Registry());
}
