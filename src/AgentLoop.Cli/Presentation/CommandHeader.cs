
namespace AgentLoop.Harness.Composition;


/// <summary>
/// Banner homogeneo que imprimen los comandos del CLI al arrancar: muestra
/// key, modelo, politica, objetivo y herramientas, sin que cada comando
/// tenga que reinventar el formato. Asi chat, run, goal y journal se
/// ven consistentes.
/// </summary>
public static class CommandHeader
{
    /// <summary>Pinta el banner en lineas "  clave : valor".</summary>
    public static void Print(string title, IEnumerable<(string Key, string Value)> lines)
    {
        Console.WriteLine(title);
        Console.WriteLine();
        foreach (var (key, value) in lines)
        {
            Console.WriteLine($"{key,-9} : {value}");
        }
        Console.WriteLine();
    }

    public static (string Key, string Value) Line(string key, string value) => (key, value);
}
