using System.Text.Json;
using AgentLoop.Provider;

namespace AgentLoop.Goals.Projects;

/// <summary>Explicit deterministic fixture, never selected for normal model runs.</summary>
public sealed class ProjectScriptClient(AdmittedProjectContract admitted, string fixtureRoot, int maxArtifactBytes) : ILLMClient
{
    private readonly Dictionary<string, int> _steps = new();
    public Task<IReadOnlyList<ModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ModelInfo>>([]);
    public Task<JsonElement> CreateChatCompletionAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var text = request.Messages.Last(m => m.Role == ChatRoles.User).Content!;
        const string marker = "ESTADO AUTORITATIVO DEL PROYECTO:\n";
        var start = text.LastIndexOf(marker, StringComparison.Ordinal);
        using var brief = JsonDocument.Parse(text[(start + marker.Length)..]);
        var root = brief.RootElement;
        var work = root.GetProperty("workItemId").GetString()!;
        var attempt = root.GetProperty("attemptId").GetString()!;
        var step = _steps.GetValueOrDefault(attempt);
        _steps[attempt] = step + 1;
        string name;
        object args;
        var item = admitted.Contract.WorkItems.SingleOrDefault(w => w.Id == work);
        if (item is not null && step == 0) { name = "tarjeta"; args = new { nombre = "abrir", objetivo = "entregable " + work, plan = "crear y validar", work_item_id = work, attempt_id = attempt }; }
        else if (item is not null && step <= item.ArtifactIds.Count)
        {
            var spec = admitted.Contract.Artifacts.Single(a => a.Id == item.ArtifactIds[step - 1]);
            name = "write_text_file";
            var source = Path.GetFullPath(spec.RelativePath, fixtureRoot);
            ProjectContractAdmission.CheckResource(source);
            if (new FileInfo(source).Length > maxArtifactBytes) throw new IOException("Fixture excede politica de artefactos.");
            args = new { path = Path.GetFullPath(spec.RelativePath, admitted.Root), content = File.ReadAllText(source) };
        }
        else
        {
            var phase = item is null ? step : step - item.ArtifactIds.Count - 1;
            if (phase == 0) { name = "project_validate"; args = new { work_item_id = work, attempt_id = attempt }; }
            else if (phase == 1)
            {
                var result = request.Messages.Last(m => m.Role == ChatRoles.Tool && m.Name == "project_validate");
                using var data = JsonDocument.Parse(result.Content!);
                if (!data.RootElement.TryGetProperty("receipt_id", out var receipt)) throw new InvalidOperationException("Validador fallo en fixture; no se declara exito.");
                name = "tarjeta"; args = new { nombre = "comprobar", comprobacion = "validadores host", resultado = "criterios satisfechos", evidencia_id = receipt.GetString(), work_item_id = work, attempt_id = attempt };
            }
            else { name = "tarjeta"; args = new { nombre = "cerrar", resultado = "entregable comprobado", evidencia = "recibos y validadores host", work_item_id = work, attempt_id = attempt }; }
        }
        var call = new { id = Guid.NewGuid().ToString("N"), type = "function", function = new { name, arguments = JsonSerializer.Serialize(args) } };
        return Task.FromResult(JsonSerializer.SerializeToElement(new { model = request.Model, choices = new[] { new { finish_reason = "tool_calls", message = new { role = "assistant", tool_calls = new[] { call } } } } }));
    }
    public async IAsyncEnumerable<JsonElement> StreamChatCompletionAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    { await Task.Yield(); yield break; }
}
