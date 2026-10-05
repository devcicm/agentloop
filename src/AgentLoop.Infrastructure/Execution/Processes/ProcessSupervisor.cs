using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
namespace AgentLoop.Terminals;


/// <summary>
/// Lanza y vigila procesos sin bloquear a quien lo llama.
///
/// Nada aqui espera de forma sincrona: arrancar devuelve un handle al instante, la salida
/// llega por eventos del propio Process (hilos del pool) y el fin del proceso se anuncia
/// abriendo el latch del handle. El agente puede lanzar tres comandos, seguir razonando y
/// enterarse de los tres cuando lleguen.
///
/// La salida no se publica linea a linea: eso ahogaria al modelo en un build ruidoso. Se
/// publica un aviso agrupado cada <see cref="OutputNotifyInterval"/> con cuantas lineas hay
/// nuevas, y el agente decide si las lee. Lo que si se publica siempre es lo que cambia el
/// estado: arranque, fin y patrones vigilados.
/// </summary>
public sealed class ProcessSupervisor : IProcessSupervisor, IDisposable
{
    private readonly EventBus _bus;
    private readonly ConcurrentDictionary<string, ProcessHandle> _handles = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastNotice = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _sinceNotice = new(StringComparer.Ordinal);
    private readonly List<OutputWatcher> _watchers = [];
    private readonly object _watcherGate = new();
    private int _counter;
    private int _watcherCounter;
    private readonly SemaphoreSlim _capacity;

    public ProcessSupervisor(EventBus bus, ExecutionPolicy? policy = null)
    {
        _bus = bus;
        Policy = policy ?? ExecutionPolicy.Default;
        if (Policy.MaxConcurrent < 1) throw new ArgumentOutOfRangeException(nameof(policy));
        _capacity = new SemaphoreSlim(Policy.MaxConcurrent, Policy.MaxConcurrent);
    }

    IReadOnlyList<IProcessHandle> IProcessSupervisor.Handles => Handles;
    IReadOnlyList<IOutputWatcher> IProcessSupervisor.Watchers => Watchers;
    IProcessHandle? IProcessSupervisor.Find(string id) => Find(id);
    async Task<IProcessHandle> IProcessSupervisor.LaunchAsync(ShellCommand command, CancellationToken token) => await LaunchAsync(command, token);
    IOutputWatcher IProcessSupervisor.Watch(string pattern, string? processId) => Watch(pattern, processId);

    public ExecutionPolicy Policy { get; }

    /// <summary>
    /// Puerta de ejecucion: abierta deja pasar, cerrada encola. Es un reset event manual
    /// porque una pausa afecta a todos los que quieran arrancar algo, no a uno solo.
    /// </summary>
    public AsyncManualResetEvent Puerta { get; } = new(signaled: true);

    public TimeSpan OutputNotifyInterval { get; init; } = TimeSpan.FromSeconds(1);

    public int MaxBufferedLines { get; init; } = 2000;

    public int Running => _handles.Values.Count(h => h.Running);

    public int Total => _handles.Count;

    public IReadOnlyList<ProcessHandle> Handles => _handles.Values.OrderBy(h => h.Id, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<OutputWatcher> Watchers
    {
        get
        {
            lock (_watcherGate)
            {
                return _watchers.ToArray();
            }
        }
    }

    public ProcessHandle? Find(string id) => _handles.TryGetValue(id.Trim(), out var handle) ? handle : null;

    public void Pausar()
    {
        Puerta.Reset();
        _bus.Publish(AgentEventKind.Aviso, "supervisor", "puerta cerrada: las ejecuciones nuevas quedan en espera");
    }

    public void Reanudar()
    {
        Puerta.Set();
        _bus.Publish(AgentEventKind.Aviso, "supervisor", "puerta abierta: las ejecuciones nuevas vuelven a pasar");
    }

    /// <summary>
    /// Arranca un proceso y vuelve enseguida. Espera a la puerta si esta cerrada, que es la
    /// unica espera posible aqui y es cooperativa.
    /// </summary>
    public async Task<ProcessHandle> LaunchAsync(ShellCommand command, CancellationToken cancellationToken = default)
    {
        var decision = await Policy.EvaluateAsync(command, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            _bus.Publish(AgentEventKind.Error, "politica", $"comando rechazado: {decision.Reason}", new { comando = command.Command });
            throw new InvalidOperationException(decision.Reason);
        }

        if (decision.Simulated)
        {
            throw new InvalidOperationException("modo simulacion: los procesos en segundo plano no se lanzan.");
        }

        await Puerta.WaitAsync(cancellationToken).ConfigureAwait(false);
        var startInfo = command.ToStartInfo(Policy.WorkingDirectory);
        if (!await _capacity.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException($"capacidad agotada (tope {Policy.MaxConcurrent} procesos)");

        var id = $"p{Interlocked.Increment(ref _counter)}";
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        var handle = new ProcessHandle(id, command, process, MaxBufferedLines);

        process.OutputDataReceived += (_, e) => OnLine(handle, e.Data, isError: false);
        process.ErrorDataReceived += (_, e) => OnLine(handle, e.Data, isError: true);
        process.Exited += (_, _) => OnExited(handle, process);

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            _capacity.Release();
            _bus.Publish(AgentEventKind.Error, "supervisor", $"no se pudo arrancar {command.Shell}: {ex.Message}");
            throw new InvalidOperationException($"no se pudo arrancar {command.Shell}: {ex.Message}", ex);
        }

        _handles[id] = handle;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.StandardInput.Close();

        _bus.Publish(
            AgentEventKind.ProcesoIniciado,
            id,
            $"arranca {command}",
            new { proceso = id, pid = handle.Pid, shell = command.Shell.ToString(), etiqueta = command.Label });

        return handle;
    }

    /// <summary>
    /// Lanza y espera el latch de fin, con tope. El await no ocupa hilo: mientras tanto el
    /// resto del sistema sigue publicando eventos.
    /// </summary>
    public async Task<ShellResult> RunAsync(
        ShellCommand command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (Policy.Mode == PolicyMode.Simular)
        {
            var decision = await Policy.EvaluateAsync(command, cancellationToken).ConfigureAwait(false);
            if (!decision.Allowed) throw new InvalidOperationException(decision.Reason);
            _bus.Publish(AgentEventKind.Aviso, "politica", $"simulado (no se ejecuta): {command}");
            return new ShellResult("(simulado)", command, null, false, true, TimeSpan.Zero, [], false);
        }

        var handle = await LaunchAsync(command, cancellationToken).ConfigureAwait(false);
        var completed = await handle.Terminado.WaitAsync(Policy.Clamp(timeout), cancellationToken).ConfigureAwait(false);

        if (!completed)
        {
            handle.Kill();
            _bus.Publish(
                AgentEventKind.Error,
                handle.Id,
                $"tope de {Policy.Clamp(timeout).TotalSeconds:0}s agotado; proceso matado");
        }

        return new ShellResult(
            handle.Id,
            command,
            handle.ExitCode,
            !completed,
            false,
            handle.Elapsed,
            handle.Tail(60),
            handle.Dropped > 0);
    }

    /// <summary>Registra un patron a vigilar sobre la salida.</summary>
    public OutputWatcher Watch(string pattern, string? processId = null)
    {
        var watcher = new OutputWatcher($"w{Interlocked.Increment(ref _watcherCounter)}", pattern, processId);

        lock (_watcherGate)
        {
            if (_watchers.Count >= 256) throw new InvalidOperationException("tope de 256 observadores; retira los anteriores");
            _watchers.Add(watcher);
        }

        _bus.Publish(AgentEventKind.Aviso, "supervisor", $"vigilando {watcher}");
        return watcher;
    }

    public bool Kill(string id)
    {
        var handle = Find(id);
        if (handle is null)
        {
            return false;
        }

        var killed = handle.Kill();
        if (killed)
        {
            _bus.Publish(AgentEventKind.Aviso, id, "matado a peticion del agente");
        }

        return killed;
    }

    public bool RemoveWatcher(string id)
    {
        lock (_watcherGate) return _watchers.RemoveAll(w => w.Id == id) > 0;
    }

    public void Dispose()
    {
        foreach (var handle in _handles.Values)
        {
            handle.Dispose();
        }

        _handles.Clear();
    }

    private void OnLine(ProcessHandle handle, string? text, bool isError)
    {
        if (text is null)
        {
            return; // fin del flujo, no es una linea
        }

        var line = handle.Append(isError, text);

        lock (_watcherGate)
        {
            foreach (var watcher in _watchers)
            {
                if (watcher.TryMatch(handle.Id, line))
                {
                    _bus.Publish(
                        AgentEventKind.Observador,
                        handle.Id,
                        $"el patron {watcher.Pattern} aparecio: {Trim(line.Text)}",
                        new { observador = watcher.Id, proceso = handle.Id, linea = line.Text });
                }
            }
            _watchers.RemoveAll(w => w.Fired);
        }

        NotifyOutput(handle);
    }

    /// <summary>
    /// Avisa de salida nueva como mucho una vez por intervalo y por proceso. Un aviso por
    /// linea convertiria un build normal en miles de eventos y el agente no veria nada mas.
    /// </summary>
    private void NotifyOutput(ProcessHandle handle)
    {
        var pending = _sinceNotice.AddOrUpdate(handle.Id, 1, (_, current) => current + 1);
        var last = _lastNotice.GetValueOrDefault(handle.Id, DateTimeOffset.MinValue);

        if (DateTimeOffset.Now - last < OutputNotifyInterval)
        {
            return;
        }

        _lastNotice[handle.Id] = DateTimeOffset.Now;
        _sinceNotice[handle.Id] = 0;

        var tail = handle.Tail(1).FirstOrDefault();
        _bus.Publish(
            AgentEventKind.ProcesoSalida,
            handle.Id,
            $"{pending} linea(s) nuevas, {handle.Unread} sin leer. Ultima: {Trim(tail?.Text ?? string.Empty)}",
            new { proceso = handle.Id, nuevas = pending, sin_leer = handle.Unread });
    }

    private void OnExited(ProcessHandle handle, Process process)
    {
        int? code = null;
        try
        {
            // Sin este WaitForExit los handlers de salida pueden quedarse sin vaciar y se
            // perderian las ultimas lineas, justo las que suelen explicar el fallo.
            process.WaitForExit();
            code = process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or SystemException)
        {
            // El proceso ya no esta; se reporta sin codigo.
        }

        handle.RecordExit(code);
        _capacity.Release();

        _bus.Publish(
            AgentEventKind.ProcesoFin,
            handle.Id,
            $"termina con codigo {code?.ToString() ?? "desconocido"} tras {handle.Elapsed.TotalSeconds:0.0}s ({handle.Captured} lineas, {handle.Unread} sin leer)",
            new { proceso = handle.Id, codigo = code, lineas = handle.Captured, sin_leer = handle.Unread });

        handle.Release();
    }

    private static string Trim(string text) => text.Length > 160 ? text[..160] + "..." : text;
}
