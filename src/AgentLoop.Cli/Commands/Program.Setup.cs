using System.Text;
using AgentLoop.Configuration;

namespace AgentLoop;

internal static partial class Program
{
    /// <summary>Wizard del operador para configurar credenciales sin eco ni contexto LLM.</summary>
    private static int SetupCredentials(string[] args)
    {
        if (args.Length > 2 || (args.Length == 2 && args[1] != "--status"))
            return Fail("Uso: setup [--status]. Nunca pases una clave como argumento.");
        try
        {
            ConsoleUi.Section("SETUP / Credenciales locales");
            var configured = SecretStore.TryGetLLMApiKey(out _, out var source);
            ConsoleUi.Field("NVIDIA", configured ? "configurada; valor oculto" : "sin configurar");
            if (configured) ConsoleUi.Field("Origen", source);
            ConsoleUi.Field("Destino", LocalCredentialWriter.Destination);
            ConsoleUi.Text("GitHub usa gh auth login y su almacen del sistema; no copies el token a este proyecto.");
            if (args.Length == 2) return 0;
            if (Console.IsInputRedirected || Console.IsOutputRedirected)
                return Fail("El wizard exige una consola interactiva para la entrada oculta. Usa setup --status para consultar.");
            ConsoleUi.Text("Archivo local en texto plano con permisos restringidos. No se envia al modelo ni se prueba por red.");
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SecretStore.ApiKeyEnvVar)))
                ConsoleUi.Text("NVIDIA_API_KEY tiene prioridad: guardar un archivo no reemplaza esa variable.", ConsoleColor.Yellow);
            var replace = File.Exists(LocalCredentialWriter.Destination);
            ConsoleUi.Text(replace ? "Escribe REEMPLAZAR para sustituir la clave existente; Enter cancela." : "Escribe CONFIGURAR para guardar una clave; Enter cancela.");
            if (Console.ReadLine() != (replace ? "REEMPLAZAR" : "CONFIGURAR")) { ConsoleUi.Text("Sin cambios."); return 0; }
            ConsoleUi.Text("Pega la clave (entrada oculta). Enter guarda; Escape cancela.");
            var secret = new StringBuilder();
            try
            {
                while (true)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Escape || (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))) { ConsoleUi.Text("Sin cambios."); return 0; }
                    if (key.Key == ConsoleKey.Enter) break;
                    if (key.Key == ConsoleKey.Backspace) { if (secret.Length > 0) secret.Length--; continue; }
                    if (char.IsControl(key.KeyChar)) continue;
                    if (secret.Length >= 4096) return Fail("Credencial demasiado larga; sin cambios.");
                    secret.Append(key.KeyChar);
                }
                LocalCredentialWriter.Save(secret.ToString(), replace);
            }
            finally { secret.Clear(); }
            ConsoleUi.Status("Credencial guardada. El siguiente objetivo usa un cliente nuevo. Consulta setup --status.", true);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            return Fail("No se pudo acceder al almacen local o comprobar Git. No se mostraran datos de la credencial.");
        }
    }
}
