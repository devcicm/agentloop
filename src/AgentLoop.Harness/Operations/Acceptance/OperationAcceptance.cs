using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AgentLoop.Goals;


/// <summary>Observaciones producidas por herramientas y criterios fijados al admitir el objetivo.</summary>
public sealed class OperationAcceptance
{
    private readonly CardLedger _ledger;
    private readonly List<ArtifactCriterion> _criteria;
    private readonly Dictionary<string, string> _baseline = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ObservationReceipt> _receipts = [];
    private readonly Dictionary<string, string> _checkedHashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _observedHashes = new(StringComparer.OrdinalIgnoreCase);
    private string? _verifiedTransaction;
    public bool HasCriteria => _criteria.Count > 0;
    public IReadOnlyList<ObservationReceipt> Receipts => _receipts;

    public OperationAcceptance(CardLedger ledger, IEnumerable<ArtifactCriterion>? criteria = null)
    {
        _ledger = ledger;
        _criteria = (criteria ?? AcceptanceCriteria.DerivePaths(ledger.AcceptanceObjective ?? "")
            .Select(path => new ArtifactCriterion(path))).Select(c => c with { Path = System.IO.Path.GetFullPath(c.Path, ledger.WorkingDirectory) }).ToList();
        foreach (var criterion in _criteria) _baseline[criterion.Path] = ledger.Evidence.Verify(criterion.Path).Hash;
    }

    public ObservationReceipt? Observe(ToolExecution execution, string? transactionId)
    {
        if (execution.Failed || transactionId is null || execution.ToolName is "tarjeta" or "esperar" or "lanzar" or "flujo" or "matar" or "observar") return null;
        using var output = JsonDocument.Parse(execution.Output);
        var root = output.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("preview", out var preview) && preview.ValueKind == JsonValueKind.True) return null;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("simulado", out var simulated) && simulated.ValueKind == JsonValueKind.True) return null;
        var receipt = new ObservationReceipt(Guid.NewGuid().ToString("N"), transactionId, execution.ToolName,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(execution.Output))), DateTimeOffset.UtcNow);
        _receipts.Add(receipt);
        _verifiedTransaction = null; // Toda observacion nueva obliga a volver a comprobar.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.String)
        {
            var absolute = System.IO.Path.GetFullPath(path.GetString()!, _ledger.WorkingDirectory);
            _observedHashes[absolute] = _ledger.Evidence.Verify(absolute).Hash;
        }
        if (execution.ToolName == "ejecutar")
            foreach (var criterion in _criteria)
                _observedHashes[criterion.Path] = _ledger.Evidence.Verify(criterion.Path).Hash;
        return receipt;
    }

    public string? ValidateCard(CardKind kind, IReadOnlyDictionary<string, string> fields)
    {
        if (kind is not (CardKind.Comprobacion or CardKind.Cierre)) return null;
        var transaction = _ledger.Current?.Id;
        if (kind == CardKind.Comprobacion)
        {
            if (!fields.TryGetValue("evidencia_id", out var id)
                || !_receipts.Any(r => r.Id == id && r.TransactionId == transaction))
                return "comprobar exige evidencia_id de una herramienta exitosa de esta transaccion";
            _checkedHashes.Clear();
            foreach (var criterion in _criteria)
            {
                var entry = _ledger.Evidence.Verify(criterion.Path);
                if (!entry.Ok) return "artefacto inexistente o vacio: " + criterion.Path;
                if (!_observedHashes.TryGetValue(criterion.Path, out var observed) || observed != entry.Hash)
                    return "artefacto no observado con herramientas en esta operacion: " + criterion.Path;
                if (criterion.MustChange && _baseline[criterion.Path] == entry.Hash)
                    return "artefacto sin cambio respecto al inicio: " + criterion.Path;
                if (criterion.Contains is not null && !_ledger.Evidence.ReadText(criterion.Path).Contains(criterion.Contains, StringComparison.Ordinal))
                    return "contenido requerido ausente: " + criterion.Path;
                if (criterion.ValidJson)
                {
                    try { using var document = JsonDocument.Parse(_ledger.Evidence.ReadText(criterion.Path)); }
                    catch (JsonException) { return "artefacto JSON invalido: " + criterion.Path; }
                }
                _checkedHashes[criterion.Path] = entry.Hash;
            }
            _verifiedTransaction = transaction;
        }
        else
        {
            if (transaction != _verifiedTransaction) return "se requiere una comprobacion vigente de esta transaccion";
            foreach (var criterion in _criteria)
                if (!_checkedHashes.TryGetValue(criterion.Path, out var verified)
                    || _ledger.Evidence.Verify(criterion.Path).Hash != verified)
                    return "la evidencia cambio despues de comprobar: " + criterion.Path;
        }
        return null;
    }
}
