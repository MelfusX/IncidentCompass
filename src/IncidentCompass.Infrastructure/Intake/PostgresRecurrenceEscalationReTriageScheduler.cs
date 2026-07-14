using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresRecurrenceEscalationReTriageScheduler(
    PostgresDataSourceProvider dataSourceProvider,
    PostgresIntakeTransactionContext transactionContext,
    ITriageArtifactRepository artifactRepository,
    TimeProvider timeProvider) : IRecurrenceEscalationReTriageScheduler
{
    public async Task ScheduleAsync(
        TriageJob recurrenceJob,
        Fault recurrenceFault,
        RecurrenceState recurrenceState,
        CancellationToken cancellationToken)
    {
        if (recurrenceFault.RecurrenceOf is null ||
            !recurrenceState.EscalationIntentCreatedFor(recurrenceJob.Id))
        {
            return;
        }

        if (!transactionContext.HasCurrent)
        {
            throw new InvalidOperationException("Recurrence re-triage scheduling requires an intake transaction.");
        }

        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        var transaction = lease.Transaction ?? throw new InvalidOperationException("Intake transaction was not available.");
        var predecessor = await PostgresReTriagePredecessorFinder.FindAndLockAsync(
            lease.Connection, transaction, recurrenceFault.Id, cancellationToken);
        if (predecessor is null)
        {
            return;
        }

        var prior = predecessor.Value;
        var job = await InsertPendingJobAsync(
            lease.Connection, transaction, prior.FaultId, recurrenceJob, prior.ReportId, cancellationToken);
        if (job is null)
        {
            return;
        }

        var sourceArtifacts = await LoadSourceArtifactsAsync(lease.Connection, transaction, recurrenceJob.Id, cancellationToken);
        foreach (var sourceArtifact in sourceArtifacts)
        {
            await artifactRepository.InsertAsync(
                new TriageArtifact(
                    Guid.NewGuid(),
                    job.Id,
                    Attempt: null,
                    sourceArtifact.Kind,
                    sourceArtifact.DomainRef,
                    sourceArtifact.Payload,
                    sourceArtifact.ContentHash,
                    timeProvider.GetUtcNow()),
                cancellationToken);
        }

        await artifactRepository.InsertAsync(CreatePriorReportArtifact(job.Id, prior), cancellationToken);
    }

    private async Task<TriageJob?> InsertPendingJobAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid targetFaultId,
        TriageJob recurrenceJob,
        Guid predecessorReportId,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var now = timeProvider.GetUtcNow();
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_jobs (
                id, fault_id, status, attempt, config_hash, created_at_utc, updated_at_utc,
                retriage_trigger_job_id, supersedes_report_id)
            VALUES (
                @id, @fault_id, 'Pending', 1, @config_hash, @now, @now,
                @trigger_job_id, @supersedes_report_id)
            ON CONFLICT (retriage_trigger_job_id) WHERE retriage_trigger_job_id IS NOT NULL DO NOTHING
            RETURNING id;
            """, connection, transaction);
        command.AddParameter("id", id);
        command.AddParameter("fault_id", targetFaultId);
        command.AddParameter("config_hash", recurrenceJob.ConfigHash);
        command.AddParameter("now", now);
        command.AddParameter("trigger_job_id", recurrenceJob.Id);
        command.AddParameter("supersedes_report_id", predecessorReportId);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
        {
            return null;
        }

        return new TriageJob(id, targetFaultId, TriageJobStatus.Pending, 1, null, null, null, null, null,
            recurrenceJob.ConfigHash, now, now)
        {
            ReTriageTriggerJobId = recurrenceJob.Id,
            SupersedesReportId = predecessorReportId
        };
    }

    private static async Task<IReadOnlyList<(ArtifactKind Kind, string? DomainRef, JsonElement Payload, string ContentHash)>> LoadSourceArtifactsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid sourceJobId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT kind, domain_ref, redacted_payload::text, content_hash
            FROM incidentcompass.triage_artifacts
            WHERE job_id = @job_id
              AND attempt IS NULL
              AND kind IN ('TriggerSignal', 'NeighborSet', 'RecurrenceState')
            ORDER BY created_at_utc, id;
            """, connection, transaction);
        command.AddParameter("job_id", sourceJobId);
        var artifacts = new List<(ArtifactKind, string?, JsonElement, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            using var payload = JsonDocument.Parse(reader.GetString(2));
            artifacts.Add((
                Enum.Parse<ArtifactKind>(reader.GetString(0)),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                payload.RootElement.Clone(),
                reader.GetString(3)));
        }

        return artifacts;
    }

    private TriageArtifact CreatePriorReportArtifact(
        Guid jobId,
        (Guid FaultId, Guid ReportId, string Summary, string[] Limitations) predecessor)
    {
        var payload = new JsonObject
        {
            ["summary"] = predecessor.Summary,
            ["limitations"] = new JsonArray(predecessor.Limitations.Select(limitation => JsonValue.Create(limitation) as JsonNode).ToArray()),
            ["trust"] = "untrusted-prior-hypothesis"
        };
        var canonicalPayload = CanonicalJsonSerializer.Canonicalize(payload);
        using var document = JsonDocument.Parse(payload.ToJsonString());
        return new TriageArtifact(
            Guid.NewGuid(),
            jobId,
            Attempt: null,
            ArtifactKind.PriorReport,
            $"report:{predecessor.ReportId}",
            document.RootElement.Clone(),
            CanonicalJsonSerializer.ComputeSha256Hex(canonicalPayload),
            timeProvider.GetUtcNow());
    }
}
