namespace AgentLoop.Harness.Durable;

/// <summary>
/// Persistencia de Runs. La interfaz es minima para poder cambiar mañana
/// el backend (SQLite, Redis, S3) sin tocar el resto del harness.
///
/// SaveAsync se invoca desde el GoalRunner en checkpoints. LoadAsync se
/// usa cuando el CLI recibe un --resume &lt;runId&gt;. ListAsync sirve para
/// inspeccion (el comando 'journeys' podria listar los Runs recientes).
/// </summary>
public interface IRunStore
{
    Task SaveAsync(RunSnapshot snapshot, CancellationToken cancellationToken = default);

    Task<RunSnapshot?> LoadAsync(Guid runId, CancellationToken cancellationToken = default);

    IAsyncEnumerable<Guid> ListAsync(CancellationToken cancellationToken = default);
}
