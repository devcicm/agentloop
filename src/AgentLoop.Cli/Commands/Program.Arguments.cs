using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
namespace AgentLoop;

internal static partial class Program
{

    /// <summary>
    /// Une todo lo posicional en un solo prompt. Sin esto, 'chat el clima de hoy' sin
    /// comillas llegaba al modelo como 'el' y la respuesta parecia culpa del LLM.
    /// Las opciones --x se saltan junto con su valor, vengan antes o despues del texto.
    /// </summary>
    private static string PositionalOrDefault(string[] args, string fallback)
    {
        var words = new List<string>();

        for (var i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                if (BooleanFlags.Contains(args[i], StringComparer.OrdinalIgnoreCase))
                {
                    continue; // no lleva valor: lo de detras sigue siendo prompt
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    i++; // la opcion se lleva su valor: --model qwen/... no es prompt
                }

                continue;
            }

            words.Add(args[i]);
        }

        return words.Count == 0 ? fallback : string.Join(' ', words);
    }


    /// <summary>
    /// Aplica --system / --system-file / --var al hilo. Se hace antes de cualquier
    /// llamada de red: una plantilla mal escrita debe fallar gratis, no a mitad de turno.
    /// </summary>
    private static void ApplyPromptOptions(ConversationSession thread, string[] args)
    {
        var systemFile = StringOption(args, "--system-file");
        if (systemFile is not null)
        {
            var path = Path.GetFullPath(systemFile);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"No existe la plantilla de sistema: {path}");
            }

            thread.UseSystemTemplate(PromptTemplate.Parse(File.ReadAllText(path)));
        }

        var system = StringOption(args, "--system");
        if (system is not null)
        {
            thread.UseSystemTemplate(PromptTemplate.Parse(system));
        }

        foreach (var assignment in StringOptions(args, "--var"))
        {
            thread.Variables.SetAssignment(assignment);
        }
    }


    /// <summary>Igual que StringOption pero para opciones repetibles como --var.</summary>
    private static IEnumerable<string> StringOptions(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                yield return args[++i];
            }
        }
    }


    /// <summary>Opciones sin valor: hay que conocerlas para no comerse el prompt que va detras.</summary>
    private static readonly string[] BooleanFlags = ["--stream", "--no-stream", "--sin-check", "--skip-model-check", "--no-shell", "--json", "--allow-existing", "--fixture"];


    private static bool HasFlag(string[] args, string name)
        => args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));


    /// <summary>--stream, o --no-stream para apagarlo cuando el defecto lo trae puesto.</summary>
    private static bool ResolveStreaming(string[] args, bool fallback = false)
        => HasFlag(args, "--no-stream") ? false : HasFlag(args, "--stream") || fallback;


    private static string? StringOption(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }


    private static int IntOption(string[] args, string name, int fallback)
        => int.TryParse(StringOption(args, name), out var value) ? value : fallback;


    private static string Shorten(string value, int max)
        => value.Length <= max ? value : value[..max] + "...";


    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine();
        PrintHelp();
        return 64;
    }

}
