using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.ModelClients;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class AnalysisDelegateExecutor(
    ITriageArtifactRepository artifactRepository,
    TriageLedgerAppender ledgerAppender,
    ITriageLedgerReader ledgerReader,
    WorkerRoleRunner workerRoleRunner,
    TimeProvider timeProvider)
{
    public async Task<string> ExecuteAsync(
        TriageJob job,
        TriageConfiguration configuration,
        TriageJobInvestigationContext context,
        AiToolCall toolCall,
        DateTimeOffset attemptStartedAtUtc,
        CancellationToken cancellationToken)
    {
        var (roleName, task) = ReadDelegateArguments(toolCall.Arguments);
        if (!configuration.Roles.TryGetValue(roleName, out var role))
        {
            return JsonSerializer.Serialize(new { errorCode = "unknown_role", role = roleName });
        }

        await EnsureWorkerBudgetAsync(job, configuration, roleName, cancellationToken);
        await ledgerAppender.AppendAsync(job, TriageLedgerEventType.Delegated, roleName, "delegate", task, null, cancellationToken);
        await ledgerAppender.AppendBudgetEventAsync(
            job,
            "worker_started: accepted delegated worker for this attempt.",
            tokensDelta: null,
            workersDelta: 1,
            cancellationToken: cancellationToken);

        var workerContent = await workerRoleRunner.RunAsync(
            job,
            configuration,
            context,
            roleName,
            role,
            task,
            attemptStartedAtUtc,
            cancellationToken);
        var artifact = await InsertWorkerOutputArtifactAsync(job, roleName, workerContent, cancellationToken);
        var delegateResult = WorkerDelegateResultFactory.Create(roleName, workerContent, artifact.Id);

        await ledgerAppender.AppendAsync(
            job,
            TriageLedgerEventType.WorkerCompleted,
            roleName,
            toolName: null,
            delegateResult.Rationale,
            $"artifact:{artifact.Id}",
            cancellationToken);

        return delegateResult.SerializedPayload;
    }

    private async Task EnsureWorkerBudgetAsync(
        TriageJob job,
        TriageConfiguration configuration,
        string roleName,
        CancellationToken cancellationToken)
    {
        var usage = await ledgerReader.ReadBudgetUsageAsync(job, cancellationToken);
        if (usage.WorkerCalls < configuration.Orchestrator.Budget.MaxWorkers)
        {
            return;
        }

        await ledgerAppender.AppendBudgetEventAsync(
            job,
            "max_workers_reached: attempt worker budget was already reached.",
            tokensDelta: null,
            workersDelta: null,
            cancellationToken: cancellationToken);
        throw new InvalidOperationException("The triage attempt worker budget was reached before delegation.");
    }

    private async Task<TriageArtifact> InsertWorkerOutputArtifactAsync(
        TriageJob job,
        string roleName,
        string content,
        CancellationToken cancellationToken)
    {
        var payload = JsonNode.Parse(content) ?? new JsonObject { ["raw"] = content };
        var canonicalPayload = CanonicalJsonSerializer.Canonicalize(payload);
        var artifact = new TriageArtifact(
            Guid.NewGuid(),
            job.Id,
            job.Attempt,
            ArtifactKind.WorkerOutput,
            $"worker:{roleName}",
            CanonicalJsonSerializer.ToElement(payload),
            CanonicalJsonSerializer.ComputeSha256Hex(canonicalPayload),
            timeProvider.GetUtcNow());

        await artifactRepository.InsertAsync(artifact, cancellationToken);
        return artifact;
    }

    private static (string Role, string Task) ReadDelegateArguments(JsonElement arguments)
    {
        var root = JsonElementReader.RequireObject(
            arguments,
            "delegate arguments must be an object.",
            CreateException);
        var role = ReadDelegateString(root, "role");
        var task = ReadDelegateString(root, "task");
        return (role, task);
    }

    private static string ReadDelegateString(JsonElement arguments, string propertyName)
    {
        return JsonElementReader.ReadRequiredString(
            arguments,
            propertyName,
            $"delegate is missing string {propertyName}.",
            CreateException,
            trim: false);
    }

    private static Exception CreateException(string message)
    {
        return new DelegateToolCallValidationException(message);
    }
}
