namespace AgentLoop.Harness.Durable;


/// <summary>
/// Foto serializable de un Run en un instante dado: lo necesario para
/// reanudar despues de un crash o Ctrl-C. Deliberadamente guarda los
/// Steps completos y los Metrics, no solo el estado del reactor: la
/// inspeccion post-mortem vale mas que reanudar exactamente desde el
/// ultimo ciclo.
///
/// Versionado por si el formato cambia: si subimos Major, los snapshots
/// viejos se rechazan en LoadAsync. Los campos opcionales con valor por
/// defecto (como Evidence) NO suben version: los snapshots viejos que no
/// los traen deserializan con el valor por defecto.
/// </summary>
public sealed record RunSnapshot(
    int Version,
    Guid RunId,
    string Objective,
    RunStatus Status,
    string? FailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    IReadOnlyList<Step> Steps,
    IReadOnlyDictionary<string, long> Metrics,
    EvidenceSnapshot? Evidence = null)
{
    public const int CurrentVersion = 1;
}
