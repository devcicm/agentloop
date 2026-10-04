using AgentLoop.Harness.Core;
using AgentLoop.Provider;

namespace AgentLoop.Harness.Runner;

/// <summary>Opciones que afinan como se ejecuta un Run.</summary>
public sealed class RunnerOptions
{
    /// <summary>Optional project contract. Execution requires the isolated backend preflight.</summary>
    public string? ProjectContractPath { get; init; }
    public AgentLoop.Harness.Composition.ProjectServices? ProjectServices { get; init; }

    /// <summary>Presupuesto de solicitudes al LLM; nombre heredado, no iteraciones de estados.</summary>
    public int MaxIterations { get; init; } = 80;

    /// <summary>Deadline en segundos. Por defecto 300 (5 minutos).</summary>
    public int DeadlineSeconds { get; init; } = 300;

    /// <summary>Maximo de procesos vivos simultaneos.</summary>
    public int MaxConcurrent { get; init; } = 4;

    /// <summary>Modo del ExecutionPolicy subyacente (auto/confirmar/simular).</summary>
    public string PolicyMode { get; init; } = "auto";

    public bool AllowShell { get; init; } = true;
    public IReadOnlyList<string> AllowedPaths { get; init; } = [];

    /// <summary>Working directory del Scope. Por defecto el cwd.</summary>
    public string WorkingDirectory { get; init; } = Environment.CurrentDirectory;

    /// <summary>Si true, se emite la traza del reactor a stdout.</summary>
    public bool Verbose { get; init; }

    public int MaxTokens { get; init; } = 2048;

    public string? Model { get; init; }
    public AgentLoop.Kernel.Projects.ITokenBudgetEstimator? ContextEstimator { get; init; }

    public ThinkingPolicy? Thinking { get; init; }
}
