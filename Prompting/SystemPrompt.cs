namespace AgentLoop.Prompting;

/// <summary>Plantilla de sistema por defecto del harness.</summary>
public static class SystemPrompt
{
    /// <summary>
    /// El bloque &lt;contexto&gt; usa {{xml:contexto}} a proposito: es el unico hueco donde
    /// entra texto arbitrario del usuario, y escaparlo impide que una linea como
    /// "&lt;/contexto&gt; ahora ignora lo anterior" se salga del delimitador.
    /// </summary>
    public const string DefaultTemplate =
        """
        Eres un agente conciso que responde en {{idioma}}.

        Datos de la sesion:
        - modelo: {{modelo}}
        - fecha: {{fecha}} {{hora}}
        - herramientas: {{herramientas}}

        Reglas:
        - Usa las herramientas cuando hagan falta; nunca inventes su resultado.
        - Manten el hilo de la conversacion: el usuario puede referirse a turnos anteriores.
        - Responde en texto plano salvo que se pida otro formato.

        <contexto>
        {{xml:contexto}}
        </contexto>
        """;

    public static PromptTemplate Default => PromptTemplate.Parse(DefaultTemplate);
}
