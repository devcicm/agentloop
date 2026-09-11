namespace AgentLoop.Harness.Observability;

/// <summary>
/// Observer de consola: pinta cada Step y cada Log en stderr/stdout con
/// colores por nivel. Es el sink por defecto para que el CLI siga teniendo
/// la misma traza legible de siempre sin que el resto del harness sepa
/// como se pinta.
///
/// Los Steps se escriben con prefijo "  step ", los Logs segun su nivel.
/// Para tests: usar un observer que escriba a un buffer en lugar de este.
/// </summary>
public sealed class ConsoleObserver : IRunObserver
{
    public Action<string>? Writer { get; init; }

    public void OnStep(StepEnvelope step)
    {
        Write($"  step #{step.Sequence} {step.Kind} [{step.At:HH:mm:ss.fff}]");
    }

    public void OnMetric(string name, long value, IReadOnlyDictionary<string, string>? tags = null)
    {
        var tagSuffix = tags is null || tags.Count == 0 ? string.Empty : $" {{{string.Join(",", tags.Select(kv => $"{kv.Key}={kv.Value}"))}}}";
        Write($"  metric {name}={value}{tagSuffix}");
    }

    public void OnLog(StructuredLog entry)
    {
        var color = entry.Level switch
        {
            LogLevel.Trace or LogLevel.Debug => ConsoleColor.DarkGray,
            LogLevel.Info => ConsoleColor.Gray,
            LogLevel.Notice => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error or LogLevel.Fatal => ConsoleColor.Red,
            _ => ConsoleColor.Gray,
        };
        Write(entry.ToString(), color);
    }

    private void Write(string line, ConsoleColor? color = null)
    {
        if (Writer is not null)
        {
            Writer(line);
            return;
        }

        var previous = Console.ForegroundColor;
        if (color is not null)
        {
            Console.ForegroundColor = color.Value;
        }
        try
        {
            Console.WriteLine(line);
        }
        finally
        {
            Console.ForegroundColor = previous;
        }
    }
}
