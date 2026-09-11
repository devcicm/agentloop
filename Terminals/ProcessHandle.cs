using System.Diagnostics;
using System.Text;
using AgentLoop.Concurrency;

namespace AgentLoop.Terminals;

/// <summary>Una linea de salida capturada, con su origen.</summary>
public sealed record OutputLine(int Index, DateTimeOffset At, bool IsError, string Text)
{
    public override string ToString() => IsError ? $"err| {Text}" : $"out| {Text}";
}

/// <summary>
/// Un proceso vivo o ya terminado, con su salida capturada y el latch de finalizacion.
///
/// <see cref="Terminado"/> es un reset event manual a proposito: el fin de un proceso pasa
/// una sola vez y a partir de ahi es permanente, asi que quien pregunte tarde tiene que
/// encontrarselo abierto igual. Ademas puede haber varios esperandolo (el que lanzo el
/// comando y un observador), y un evento manual los libera a todos.
/// </summary>
public sealed class ProcessHandle : IDisposable
{
    private readonly object _gate = new();
    private readonly List<OutputLine> _lines = [];
    private readonly Process _process;
    private int _cursor;
    private int _captured;
    private int _dropped;

    internal ProcessHandle(string id, ShellCommand command, Process process, int maxLines)
    {
        Id = id;
        Command = command;
        _process = process;
        MaxLines = maxLines;
        StartedAt = DateTimeOffset.Now;
    }

    public string Id { get; }

    public ShellCommand Command { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset? EndedAt { get; private set; }

    public int? ExitCode { get; private set; }

    /// <summary>Latch de fin de proceso. Se abre una vez y se queda abierto.</summary>
    public AsyncManualResetEvent Terminado { get; } = new();

    public bool Running => !Terminado.IsSet;

    /// <summary>Tope del buffer en memoria; un proceso hablador no puede comerse la RAM.</summary>
    public int MaxLines { get; }

    public int Pid
    {
        get
        {
            try
            {
                return _process.Id;
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }
    }

    public TimeSpan Elapsed => (EndedAt ?? DateTimeOffset.Now) - StartedAt;

    /// <summary>Lineas emitidas por el proceso, incluidas las ya descartadas del buffer.</summary>
    public int Captured
    {
        get
        {
            lock (_gate)
            {
                return _captured;
            }
        }
    }

    /// <summary>Lineas que se cayeron del buffer por el tope.</summary>
    public int Dropped
    {
        get
        {
            lock (_gate)
            {
                return _dropped;
            }
        }
    }

    /// <summary>Lineas capturadas que el agente todavia no ha leido.</summary>
    public int Unread
    {
        get
        {
            lock (_gate)
            {
                return _lines.Count - _cursor;
            }
        }
    }

    internal OutputLine Append(bool isError, string text)
    {
        lock (_gate)
        {
            var line = new OutputLine(++_captured, DateTimeOffset.Now, isError, text);
            _lines.Add(line);

            if (_lines.Count > MaxLines)
            {
                var excess = _lines.Count - MaxLines;
                _lines.RemoveRange(0, excess);
                _dropped += excess;
                _cursor = Math.Max(0, _cursor - excess);
            }

            return line;
        }
    }

    /// <summary>Ultimas lineas, sin mover el cursor de lectura.</summary>
    public IReadOnlyList<OutputLine> Tail(int count)
    {
        lock (_gate)
        {
            return _lines.TakeLast(count).ToArray();
        }
    }

    /// <summary>Lo nuevo desde la ultima lectura. Mueve el cursor: cada linea se entrega una vez.</summary>
    public IReadOnlyList<OutputLine> ReadNew(int max)
    {
        lock (_gate)
        {
            var batch = _lines.Skip(_cursor).Take(max).ToArray();
            _cursor += batch.Length;
            return batch;
        }
    }

    public string TailText(int count)
    {
        var builder = new StringBuilder();
        foreach (var line in Tail(count))
        {
            builder.AppendLine(AgentLoop.Prompting.TextSanitizer.Plain(line.ToString()));
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Fija el desenlace sin abrir el latch todavia.</summary>
    internal void RecordExit(int? exitCode)
    {
        ExitCode = exitCode;
        EndedAt = DateTimeOffset.Now;
    }

    /// <summary>
    /// Abre el latch y suelta a los que esperaban. Va DESPUES de publicar el fin a
    /// proposito: si se abriera antes, quien despierta podria mirar el bus y no encontrar
    /// todavia el evento del proceso que acaba de esperar.
    /// </summary>
    internal void Release() => Terminado.Set();

    /// <summary>Mata el proceso y su descendencia. Idempotente.</summary>
    public bool Kill()
    {
        try
        {
            if (_process.HasExited)
            {
                return false;
            }

            _process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or SystemException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }

    public override string ToString()
    {
        var estado = Running
            ? $"vivo {Elapsed.TotalSeconds:0.0}s"
            : $"fin codigo {ExitCode?.ToString() ?? "?"} en {Elapsed.TotalSeconds:0.0}s";

        return $"{Id} [{estado}] {Command} ({Captured} lineas, {Unread} sin leer)";
    }
}
