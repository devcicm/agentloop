
namespace AgentLoop.Prompting;


/// <summary>
/// Contrato explicito de sanitizacion: dado el contexto donde va a acabar
/// un valor, que sanitizer aplicar. Asi el compilador no puede ayudar (los
/// strings son strings), pero la tabla si: cualquier nueva llamada que
/// concatena valor externo al prompt debe pasar por aqui y queda
/// trazada en un unico sitio.
///
/// Por que existe: el harness concatena con $"..." unas 25 veces en
/// BuildSystemPrompt, Brief, Ledger.DescribeState, Trace, Summary, etc.
/// Sin contrato, la mitad se acuerda de Plain() y la mitad no. Con
/// contrato, todas pasan por <see cref="Apply"/>.
/// </summary>
public static class SanitizationContract
{
    public static string Apply(string? value, SanitizationContext context)
    {
        return context switch
        {
            SanitizationContext.PromptPlain => TextSanitizer.Plain(value),
            SanitizationContext.PromptXmlTag => TextSanitizer.Xml(value),
            SanitizationContext.PromptJsonExample => TextSanitizer.Json(value),
            SanitizationContext.ToolResultMessage => TextSanitizer.EnsureJsonObject(value),
            SanitizationContext.ModelOutput => TextSanitizer.Plain(value),
            SanitizationContext.LedgerField => TextSanitizer.Plain(value),
            _ => TextSanitizer.Plain(value),
        };
    }
}
