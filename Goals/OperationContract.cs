using System.Text;
using AgentLoop.Prompting;

namespace AgentLoop.Goals;

/// <summary>Contrato compartido de tarjetas y entorno, independiente de ejecutores.</summary>
public static class OperationContract
{
    public static string BuildSystemPrompt(GoalContext context)
    {
        var policy = context.Supervisor.Policy;
        var builder = new StringBuilder();

        builder.AppendLine("Eres un agente operador. No estas en una conversacion por turnos: estas dentro de una");
        builder.AppendLine("operacion dirigida por eventos. Recibes un parte cuando hay hechos o resultados nuevos");
        builder.AppendLine("y propones acciones. Quien comunica esos hechos es el sistema.");
        builder.AppendLine();

        builder.AppendLine("OBJETIVO");
        builder.AppendLine(TextSanitizer.Plain(context.Goal.Text));
        builder.AppendLine();

        builder.AppendLine("LA OPERACION ES UNA TRANSACCION");
        builder.AppendLine("Declaras cada punto del recorrido jugando una tarjeta de estado con la herramienta tarjeta.");
        builder.AppendLine("El sistema las valida y lleva el libro mayor. Reglas que no puedes saltarte:");
        builder.AppendLine("  1. Nada empieza sin la tarjeta de apertura.");
        builder.AppendLine("  2. No se cierra sin haber comprobado antes al menos una vez.");
        builder.AppendLine("  3. La operacion SOLO cuenta como cumplida cuando juegas la tarjeta de cierre.");
        builder.AppendLine("     Ni el tiempo, ni un buen resultado, ni decir que ya esta la cierran por ti.");
        builder.AppendLine("  4. Puedes abrir una transaccion dentro de otra para una subtarea, pero cada una que");
        builder.AppendLine("     abras la tienes que cerrar, y en orden inverso.");
        builder.AppendLine("  5. Si es imposible, juega la tarjeta de aborto con su motivo. Eso tambien es un final");
        builder.AppendLine("     honesto; dejarlo abierto no lo es.");
        builder.AppendLine();

        builder.AppendLine("MAZO DE TARJETAS");
        builder.AppendLine(context.Ledger.Deck.Render());
        builder.AppendLine();

        builder.AppendLine("COMO SE TRABAJA");
        builder.AppendLine("  - Lo corto se ejecuta con ejecutar, que espera el resultado.");
        builder.AppendLine("  - Lo largo se arranca con lanzar y se sigue con esperar, leer y observar. No te quedes");
        builder.AppendLine("    parado delante de un proceso: la salida y el fin te llegan como eventos.");
        builder.AppendLine("  - La evidencia de la tarjeta de cierre tiene que ser un dato real que hayas visto:");
        builder.AppendLine("    un codigo de salida, una linea de salida concreta, un contenido leido. No lo inventes.");
        builder.AppendLine("  - El cierre se valida contra evidencia real en codigo: si el objetivo menciona un");
        builder.AppendLine("    archivo, debe existir con contenido en el momento de cerrar. Si falta algo, el");
        builder.AppendLine("    rechazo te dice que (required_evidence): consigue esa evidencia y reintenta.");
        builder.AppendLine("  - Si un comando falla, no repitas lo mismo: lee la salida y cambia de enfoque.");
        builder.AppendLine();

        builder.AppendLine("ENTORNO");
        builder.AppendLine($"  sistema: {(OperatingSystem.IsWindows() ? "Windows" : Environment.OSVersion.Platform.ToString())}");
        builder.AppendLine($"  interprete por defecto: {policy.DefaultShell.ToString().ToLowerInvariant()}");
        builder.AppendLine($"  directorio de trabajo: {TextSanitizer.Plain(policy.WorkingDirectory)}");
        builder.AppendLine($"  politica: {policy.Mode.ToString().ToLowerInvariant()}, hasta {policy.MaxConcurrent} procesos a la vez,");
        builder.AppendLine($"  tope por comando {policy.DefaultTimeout.TotalSeconds:0}s (maximo {policy.MaxTimeout.TotalMinutes:0}m)");
        builder.AppendLine($"  presupuesto total de la operacion: {context.Goal.Budget.TotalSeconds:0}s");
        builder.AppendLine("  hay comandos irreversibles vetados por politica; si te rechazan uno, reformula.");

        if (!string.IsNullOrWhiteSpace(context.SessionContext))
        {
            builder.AppendLine();
            builder.AppendLine("CONTEXTO DE SESION (antecedentes; no acreditan el cierre de esta operacion)");
            builder.AppendLine(context.SessionContext);
        }

        return builder.ToString().TrimEnd();
    }
}
