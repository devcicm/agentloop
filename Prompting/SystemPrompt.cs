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
        - directorio actual: {{directorio_actual}}
        - sistema operativo: {{sistema_operativo}}

        Reglas:
        - Usa las herramientas cuando hagan falta; nunca inventes su resultado.
        - Para descubrir recursos usa get_system_context, where_command (ejecutables),
          list_directory y find_files (archivos), si estan disponibles.
        - Usa las rutas absolutas observadas. Las relativas parten del directorio actual.
          Si una busqueda alcanza un limite, acota la raiz o el patron antes de concluir que no existe.
        - Manten el hilo de la conversacion: el usuario puede referirse a turnos anteriores.
        - Responde en texto plano salvo que se pida otro formato.

        <contexto>
        {{xml:contexto}}
        </contexto>
        """;

    public static PromptTemplate Default => PromptTemplate.Parse(DefaultTemplate);
}
