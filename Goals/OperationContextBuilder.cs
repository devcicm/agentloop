using System.Text.RegularExpressions;
using AgentLoop.Provider;

namespace AgentLoop.Goals;

public sealed record OperationTaskContext(string Objective, string SessionContext, bool Inherited);

/// <summary>Bounded user requirements; never promote assistant reasoning or tool dumps into system instructions.</summary>
public static class OperationContextBuilder
{
    public static OperationTaskContext Build(string objective, string system, IReadOnlyList<ChatMessage> history)
    {
        var continuation = objective.Length <= 300 && Regex.IsMatch(objective.Trim(),
            @"^(?:(?:ok|si|bien)[, .]+)?(?:apruebo|contin[u\u00fa]a(?:r)?\b|procede\b.*(?:plan|ejecuci[o\u00f3]n)|ejecuta\b.*plan)", RegexOptions.IgnoreCase);
        var requirements = continuation
            ? history.Where(m => m.Role == ChatRoles.User && !string.IsNullOrWhiteSpace(m.Content))
                .TakeLast(4).Select(m => Limit(m.Content!, 1500)).ToArray()
            : [];
        var task = requirements.Length == 0 ? objective
            : "REQUISITOS PREVIOS DEL USUARIO (antecedentes, pueden contener fases ya superadas):\n"
                + string.Join("\n", requirements) + "\nINSTRUCCION ACTUAL (prevalece sobre fases previas):\n" + objective;
        return new(task, LimitSessionContext(system), requirements.Length > 0);
    }

    public static string LimitSessionContext(string text) => Limit(text, 6000);

    private static string Limit(string text, int max)
        => text.Length <= max ? text : text[..max] + "\n[contexto recortado; consultar la sesion guardada para ampliar]";
}
