using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresReportEvidenceGrounder
{
    public async Task<IReadOnlyList<GroundedReportEvidence>> GroundAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageJob job,
        IReadOnlyList<TriageReportEvidenceReference> references,
        CancellationToken cancellationToken)
    {
        var grounded = new List<GroundedReportEvidence>();
        foreach (var reference in references)
        {
            grounded.Add(await GroundOneAsync(connection, transaction, job, reference, cancellationToken));
        }

        return grounded;
    }

    private static async Task<GroundedReportEvidence> GroundOneAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageJob job,
        TriageReportEvidenceReference reference,
        CancellationToken cancellationToken)
    {
        var artifactId = ParseArtifactId(reference.ReferenceId);
        await using var command = new NpgsqlCommand("""
            SELECT a.kind,
                   a.domain_ref,
                   a.redacted_payload::text,
                   mi.kind,
                   CASE
                       WHEN jsonb_typeof(a.redacted_payload->'score') = 'number'
                       THEN (a.redacted_payload->>'score')::double precision
                       ELSE NULL
                   END AS score
            FROM incidentcompass.triage_artifacts a
            LEFT JOIN incidentcompass.memory_items mi
              ON a.kind = 'RetrievedItem'
             AND a.domain_ref = 'memory_item:' || mi.id::text
            WHERE a.id = @artifact_id
              AND a.job_id = @job_id
              AND (a.attempt IS NULL OR a.attempt = @attempt)
              AND a.kind = ANY(ARRAY['TriggerSignal','NeighborSet','PriorReport','RetrievedItem','ToolResult'])
            LIMIT 1;
            """, connection, transaction);
        command.AddParameter("artifact_id", artifactId);
        command.AddParameter("job_id", job.Id);
        command.AddParameter("attempt", job.Attempt);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new TriageReportValidationException(
                "publish_report evidence referenceId does not resolve to a citable artifact for this job attempt.");
        }

        var artifactKind = reader.GetString(0);
        var payload = reader.GetString(2);
        var memoryKind = reader.IsDBNull(3) ? null : reader.GetString(3);
        double? score = reader.IsDBNull(4) ? null : reader.GetDouble(4);
        return new GroundedReportEvidence(
            artifactId,
            DeriveEvidenceKind(artifactKind, memoryKind),
            reference.ReferenceId,
            ValidateQuote(reference.Quote, payload),
            score);
    }

    private static Guid ParseArtifactId(string referenceId)
    {
        var normalized = referenceId.StartsWith("artifact:", StringComparison.Ordinal)
            ? referenceId["artifact:".Length..]
            : referenceId;
        if (Guid.TryParse(normalized, out var artifactId))
        {
            return artifactId;
        }

        throw new TriageReportValidationException("publish_report evidence referenceId must be a triage artifact id.");
    }

    private static string DeriveEvidenceKind(string artifactKind, string? memoryKind)
    {
        return artifactKind switch
        {
            "RetrievedItem" => memoryKind switch
            {
                "runbook" => "Runbook",
                "known_incident" => "KnownIncident",
                "operational_note" => "OperationalNote",
                "release_note" => "ReleaseNote",
                "postmortem" => "Postmortem",
                _ => "RetrievedItem"
            },
            _ => artifactKind
        };
    }

    private static string? ValidateQuote(string? quote, string redactedPayload)
    {
        if (string.IsNullOrWhiteSpace(quote))
        {
            return null;
        }

        return redactedPayload.Contains(quote, StringComparison.Ordinal) ? quote : null;
    }
}
