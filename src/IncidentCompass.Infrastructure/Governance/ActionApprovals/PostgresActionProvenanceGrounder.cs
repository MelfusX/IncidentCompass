using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Infrastructure.Investigation;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed class PostgresActionProvenanceGrounder(string? configuredTicketRepository)
{
    private readonly PostgresReportEvidenceGrounder reportGrounder = new(configuredTicketRepository);

    public async Task<GroundedActionProposalContext> GroundAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionProposalGroundingInput proposal,
        CancellationToken cancellationToken)
    {
        var origin = await FindOriginAsync(connection, transaction, proposal, cancellationToken)
            ?? throw new ActionProposalValidationException("Action proposal origin was not found.");
        await PostgresFaultTransactionLock.LockAsync(connection, transaction, origin.FaultId, cancellationToken);
        var job = await LockAndValidateOriginAsync(connection, transaction, proposal, origin, cancellationToken);
        await ValidatePersistedEvidenceSetAsync(connection, transaction, proposal, cancellationToken);
        try
        {
            await reportGrounder.GroundAsync(
                connection,
                transaction,
                job,
                proposal.EvidenceArtifactIds
                    .Select(static id => new TriageReportEvidenceReference("artifact:" + id, null))
                    .ToArray(),
                cancellationToken);
        }
        catch (TriageReportValidationException exception)
        {
            throw new ActionProposalValidationException(
                "Action provenance contains evidence that is not citable for the origin attempt.",
                exception);
        }
        var provenance = await ReadAndClassifyAsync(connection, transaction, proposal, job, cancellationToken);
        return new GroundedActionProposalContext(origin.FaultId, origin.JobId, origin.Attempt, origin.ConfigHash, provenance);
    }

    private static async Task<GroundedActionProposalContext?> FindOriginAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionProposalGroundingInput proposal,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT r.fault_id, r.job_id, j.attempt, j.config_hash
            FROM incidentcompass.triage_reports r
            JOIN incidentcompass.faults f ON f.id = r.fault_id
            JOIN incidentcompass.triage_jobs j ON j.id = r.job_id
            WHERE r.id = @report_id AND f.tenant_id = @tenant_id;
            """, connection, transaction);
        command.AddParameter("report_id", proposal.OriginReportId);
        command.AddParameter("tenant_id", proposal.TenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new GroundedActionProposalContext(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetInt32(2), reader.GetString(3), [])
            : null;
    }

    private static async Task<TriageJob> LockAndValidateOriginAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionProposalGroundingInput proposal,
        GroundedActionProposalContext origin,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT j.status, r.status, j.created_at_utc, j.updated_at_utc,
                   EXISTS (
                       SELECT 1 FROM incidentcompass.triage_ledger l
                       WHERE l.job_id = j.id AND l.attempt = j.attempt
                         AND l.event_type = 'ReportPublished'
                         AND l.payload_ref = 'report:' || r.id::text),
                   (SELECT latest.id FROM incidentcompass.triage_reports latest
                    WHERE latest.fault_id = r.fault_id
                    ORDER BY latest.created_at_utc DESC, latest.id DESC LIMIT 1)
            FROM incidentcompass.triage_reports r
            JOIN incidentcompass.triage_jobs j ON j.id = r.job_id
            WHERE r.id = @report_id AND r.fault_id = @fault_id AND j.id = @job_id
            FOR UPDATE OF r, j;
            """, connection, transaction);
        command.AddParameter("report_id", proposal.OriginReportId);
        command.AddParameter("fault_id", origin.FaultId);
        command.AddParameter("job_id", origin.JobId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            reader.GetString(0) != TriageJobStatus.Succeeded.ToString() ||
            reader.GetString(1) != TriageReportStatus.Completed.ToString() ||
            !reader.GetBoolean(4) || reader.GetGuid(5) != proposal.OriginReportId)
        {
            throw new ActionProposalValidationException("Action proposal origin is not a current completed published report.");
        }

        return new TriageJob(
            origin.JobId, origin.FaultId, TriageJobStatus.Succeeded, origin.Attempt,
            null, null, null, null, null, origin.ConfigHash,
            reader.GetDateTimeOffset(2), reader.GetDateTimeOffset(3));
    }

    private static async Task ValidatePersistedEvidenceSetAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionProposalGroundingInput proposal,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT count(DISTINCT artifact_id)
            FROM incidentcompass.triage_evidence
            WHERE report_id = @report_id AND artifact_id = ANY(@artifact_ids);
            """, connection, transaction);
        command.AddParameter("report_id", proposal.OriginReportId);
        command.AddParameter("artifact_ids", proposal.EvidenceArtifactIds.ToArray());
        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (count != proposal.EvidenceArtifactIds.Count)
        {
            throw new ActionProposalValidationException("Action provenance must use persisted origin-report evidence.");
        }
    }

    private static async Task<IReadOnlyList<ActionApprovalProvenance>> ReadAndClassifyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionProposalGroundingInput proposal,
        TriageJob job,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT id, kind, domain_ref, attempt
            FROM incidentcompass.triage_artifacts
            WHERE id = ANY(@artifact_ids) AND job_id = @job_id
            ORDER BY id;
            """, connection, transaction);
        command.AddParameter("artifact_ids", proposal.EvidenceArtifactIds.ToArray());
        command.AddParameter("job_id", job.Id);
        var rows = new List<ActionApprovalProvenance>
        {
            new(0, "report", proposal.OriginReportId, null, ActionProvenanceTrust.UntrustedPrior)
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var kind = Enum.Parse<ArtifactKind>(reader.GetString(1));
            var domainRef = reader.IsDBNull(2) ? null : reader.GetString(2);
            int? attempt = reader.IsDBNull(3) ? null : reader.GetInt32(3);
            if ((attempt is not null && attempt != job.Attempt) || !HasValidOrigin(kind, domainRef, job))
            {
                throw new ActionProposalValidationException("Action provenance artifact origin is invalid.");
            }

            rows.Add(new ActionApprovalProvenance(
                rows.Count, "artifact", reader.GetGuid(0), kind.ToString(), MapTrust(kind)));
        }

        if (rows.Count != proposal.EvidenceArtifactIds.Count + 1)
        {
            throw new ActionProposalValidationException("Action provenance contains an invalid artifact.");
        }

        return rows;
    }

    private static ActionProvenanceTrust MapTrust(ArtifactKind kind) => kind switch
    {
        ArtifactKind.TriggerSignal or ArtifactKind.NeighborSet => ActionProvenanceTrust.UntrustedSignal,
        ArtifactKind.PriorReport => ActionProvenanceTrust.UntrustedPrior,
        ArtifactKind.RetrievedItem or ArtifactKind.ToolResult => ActionProvenanceTrust.UntrustedRetrieved,
        ArtifactKind.RecurrenceState => ActionProvenanceTrust.BackendFact,
        _ => throw new ActionProposalValidationException("Artifact kind cannot ground an action proposal.")
    };

    private static bool HasValidOrigin(ArtifactKind kind, string? domainRef, TriageJob job) => kind switch
    {
        ArtifactKind.TriggerSignal => HasGuidSuffix(domainRef, "signal:"),
        ArtifactKind.NeighborSet => domainRef == "fault:" + job.FaultId,
        ArtifactKind.PriorReport => HasGuidSuffix(domainRef, "report:") || HasGuidSuffix(domainRef, "fault:"),
        ArtifactKind.RecurrenceState => domainRef == "job:" + job.Id,
        ArtifactKind.RetrievedItem => !string.IsNullOrWhiteSpace(domainRef),
        ArtifactKind.ToolResult => domainRef?.StartsWith("tool:", StringComparison.Ordinal) == true && domainRef.Length > 5,
        _ => false
    };

    private static bool HasGuidSuffix(string? value, string prefix) =>
        value?.StartsWith(prefix, StringComparison.Ordinal) == true && Guid.TryParse(value[prefix.Length..], out _);
}
