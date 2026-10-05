using System.Text.RegularExpressions;

namespace AgentLoop.Prompting;


public readonly record struct PromptPlaceholder(string Name, PromptEscaping Escaping);
