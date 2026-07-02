using System.Text.Json;
using System.Text;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal static class TriageInvestigationPromptBuilder
{
    public static string BuildOrchestratorPrompt(TriageJob job, TriageJobInvestigationContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"IncidentCompass orchestrator job {job.Id} attempt {job.Attempt}.");
        AppendContext(builder, context);
        builder.AppendLine("Delegate to the analysis role first. Then call publish_report with a minimal report_json.");
        return builder.ToString();
    }

    public static string BuildWorkerPrompt(
        string role,
        string task,
        TriageJob job,
        TriageJobInvestigationContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"IncidentCompass {role} worker task for job {job.Id} attempt {job.Attempt}.");
        builder.AppendLine("Task:");
        builder.AppendLine(task);
        AppendContext(builder, context);
        builder.AppendLine("Return only JSON matching your configured output schema.");
        return builder.ToString();
    }

    private static void AppendContext(StringBuilder builder, TriageJobInvestigationContext context)
    {
        builder.AppendLine("Fault:");
        builder.AppendLine($"- id: {context.Fault.Id}");
        builder.AppendLine($"- service: {context.Fault.ServiceName}");
        builder.AppendLine($"- environment: {context.Fault.Environment}");
        builder.AppendLine($"- fingerprintStrength: {context.Fault.FingerprintStrength}");
        builder.AppendLine("Trigger signal:");
        builder.AppendLine($"- id: {context.TriggerSignal.Id}");
        builder.AppendLine($"- summary: {context.TriggerSignal.Summary}");
        builder.AppendLine($"- errorType: {context.TriggerSignal.ErrorType}");
        builder.AppendLine($"- errorMessage: {context.TriggerSignal.ErrorMessage}");
        builder.AppendLine("Grounded artifacts:");
        foreach (var artifact in context.JobArtifacts)
        {
            builder.AppendLine($"- artifact:{artifact.Id} kind={artifact.Kind} attempt={artifact.Attempt?.ToString() ?? "job"} payload={TrimPayload(artifact.RedactedPayload)}");
        }
    }

    private static string TrimPayload(JsonElement payload)
    {
        var text = payload.GetRawText();
        return text.Length <= 800 ? text : text[..800];
    }
}
