using AgentLoop.Harness.Core;

namespace AgentLoop.Harness.Runner;

/// <summary>Opciones que afinan como se ejecuta un Run.</summary>
public sealed class RunnerOptions
{
    /// <summary>Tope duro de iteraciones del loop. Evita que un modelo que insiste gaste todo el budget.</summary>
    public int MaxIterations { get; init; } = 80;

    /// <summary>Deadline en segundos. Por defecto 300 (5 minutos).</summary>
    public int DeadlineSeconds { get; init; } = 300;

    /// <summary>Maximo de procesos vivos simultaneos.</summary>
    public int MaxConcurrent { get; init; } = 4;

    /// <summary>Politica de politica del ExecutionPolicy subyacente (auto/confirmar/simular).</summary>
    public string PolicyMode { get; init; } = "auto";

    /// <summary>Working directory del Scope. Por defecto el cwd.</summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>Si true, se emite la traza del reactor a stdout.</summary>
    public bool Verbose { get; init; }
}
