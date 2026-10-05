using System.Text.RegularExpressions;

namespace AgentLoop.Prompting;


/// <summary>Se lanza cuando la plantilla pide variables que nadie definio.</summary>
public sealed class PromptTemplateException(string message) : InvalidOperationException(message);
