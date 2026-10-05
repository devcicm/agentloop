using System.Text;
using System.Text.Json;
namespace AgentLoop.Harness.Core;


/// <summary>Serializa un Journey a markdown legible o a json para tooling.</summary>
public static class JourneyWriter
{
    public static string ToMarkdown(Journey journey)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"# Journey {journey.RunId:N}");
        builder.AppendLine();
        builder.AppendLine($"- **Objetivo**: {journey.Objective}");
        builder.AppendLine($"- **Estado final**: {journey.FinalStatus}");
        builder.AppendLine($"- **Outcome**: {journey.Outcome}");
        builder.AppendLine($"- **Inicio**: {journey.StartedAt:O}");
        builder.AppendLine($"- **Fin**: {(journey.EndedAt?.ToString("O") ?? "(sin cerrar)")}");
        builder.AppendLine($"- **Elapsed**: {journey.Elapsed.TotalSeconds:0}s");
        builder.AppendLine($"- **Pasos**: {journey.Steps.Count} (model={journey.ModelCalls}, tool={journey.ToolCalls}, wait={journey.Waits}, err={journey.Errors})");

        if (!string.IsNullOrEmpty(journey.FailureReason))
        {
            builder.AppendLine($"- **Fallo**: {journey.FailureReason}");
        }

        if (journey.Evidence is { } evidence && evidence.Files.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Evidencia");
            builder.AppendLine($"Agregado: `{evidence.AggregateHash}`");
            foreach (var file in evidence.Files)
            {
                builder.AppendLine($"- `{file.Path}` ({file.Bytes} bytes, sha `{file.Hash}`){(file.Ok ? string.Empty : " — SIN VERIFICAR")}");
            }
        }

        if (journey.Metrics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Metricas");
            foreach (var (name, value) in journey.Metrics.OrderBy(m => m.Key, StringComparer.Ordinal))
            {
                builder.AppendLine($"- `{name}` = {value}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("## Pasos");
        builder.AppendLine();
        builder.AppendLine("| # | At | Kind | Name | Elapsed | Data |");
        builder.AppendLine("|---|----|------|------|---------|------|");

        foreach (var step in journey.Steps)
        {
            var data = string.Join(", ", step.Data.Select(kv => $"{kv.Key}={kv.Value}"));
            if (data.Length > 80)
            {
                data = data[..80] + "...";
            }
            builder.AppendLine($"| {step.Sequence} | {step.At:HH:mm:ss.fff} | {step.Kind} | {step.Name} | {step.Elapsed.TotalMilliseconds:0}ms | {data} |");
        }

        return builder.ToString().TrimEnd();
    }

    public static string ToJson(Journey journey)
    {
        return JsonSerializer.Serialize(journey, new JsonSerializerOptions
        {
            WriteIndented = true,
            IncludeFields = false,
        });
    }
}
