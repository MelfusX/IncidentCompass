using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class AnalysisDelegateExecutor(
    IAiModelClient modelClient,
    ITriageArtifactRepository artifactRepository,
    TriageLedgerAppender ledgerAppender,
    TimeProvider timeProvider)
{
    public async Task<string> ExecuteAsync(
        TriageJob job,
        TriageConfiguration configuration,
        TriageJobInvestigationContext context,
        AiToolCall toolCall,
        CancellationToken cancellationToken)
    {
        var (roleName, task) = ReadDelegateArguments(toolCall.Arguments);
        if (!configuration.Roles.TryGetValue(roleName, out var role))
        {
            return JsonSerializer.Serialize(new { errorCode = "unknown_role", role = roleName });
        }

        if (role.Tools.Count > 0)
        {
            return JsonSerializer.Serialize(new { errorCode = "role_tools_not_supported_in_phase_2", role = roleName });
        }

        await ledgerAppender.AppendAsync(job, TriageLedgerEventType.Delegated, roleName, "delegate", task, null, cancellationToken);
        var workerResponse = await CompleteWorkerAsync(job, configuration, context, roleName, role, task, cancellationToken);
        AnalysisWorkerOutputSchemaValidator.Validate(workerResponse.Content, role.OutputSchema);
        var output = AnalysisWorkerOutputParser.Parse(workerResponse.Content);
        var artifact = await InsertWorkerOutputArtifactAsync(job, roleName, workerResponse.Content, cancellationToken);

        await ledgerAppender.AppendAsync(
            job,
            TriageLedgerEventType.WorkerCompleted,
            roleName,
            toolName: null,
            output.Rationale,
            $"artifact:{artifact.Id}",
            cancellationToken);

        return JsonSerializer.Serialize(new
        {
            role = roleName,
            summary = output.Rationale ?? string.Join(" ", output.KeyFacts),
            output.KeyFacts,
            output.CandidateClassification,
            output.NeedsDeeperContext,
            artifactId = artifact.Id
        });
    }

    private async Task<AiModelResponse> CompleteWorkerAsync(
        TriageJob job,
        TriageConfiguration configuration,
        TriageJobInvestigationContext context,
        string roleName,
        TriageRoleSettings role,
        string task,
        CancellationToken cancellationToken)
    {
        var route = configuration.Routes[role.RouteId];
        return await modelClient.CompleteAsync(
            new AiModelRequest(
                CorrelationId: job.Id.ToString(),
                Model: route.Model,
                Messages:
                [
                    new AiChatMessage(AiMessageRole.System, role.Instructions),
                    new AiChatMessage(
                        AiMessageRole.User,
                        TriageInvestigationPromptBuilder.BuildWorkerPrompt(roleName, task, job, context))
                ],
                Temperature: route.Temperature,
                MaxOutputTokens: route.MaxOutputTokens),
            cancellationToken);
    }

    private async Task<TriageArtifact> InsertWorkerOutputArtifactAsync(
        TriageJob job,
        string roleName,
        string content,
        CancellationToken cancellationToken)
    {
        var payload = JsonNode.Parse(content) ?? new JsonObject { ["raw"] = content };
        var canonicalPayload = CanonicalJsonSerializer.Canonicalize(payload);
        using var payloadDocument = JsonDocument.Parse(payload.ToJsonString());
        var artifact = new TriageArtifact(
            Guid.NewGuid(),
            job.Id,
            job.Attempt,
            ArtifactKind.WorkerOutput,
            $"worker:{roleName}",
            payloadDocument.RootElement.Clone(),
            CanonicalJsonSerializer.ComputeSha256Hex(canonicalPayload),
            timeProvider.GetUtcNow());

        await artifactRepository.InsertAsync(artifact, cancellationToken);
        return artifact;
    }

    private static (string Role, string Task) ReadDelegateArguments(JsonElement arguments)
    {
        var role = ReadRequiredString(arguments, "role");
        var task = ReadRequiredString(arguments, "task");
        return (role, task);
    }

    private static string ReadRequiredString(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var element) ||
            element.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(element.GetString()))
        {
            throw new InvalidOperationException($"delegate is missing string {propertyName}.");
        }

        return element.GetString()!;
    }
}
