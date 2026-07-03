using IncidentCompass.Application.Intake.Artifacts;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresTriageArtifactRepository(PostgresDataSourceProvider dataSourceProvider, PostgresIntakeTransactionContext transactionContext) : ITriageArtifactRepository
{
    public async Task InsertAsync(TriageArtifact artifact, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_artifacts (
                id, job_id, attempt, kind, domain_ref, redacted_payload, content_hash, created_at_utc)
            VALUES (
                @id, @job_id, @attempt, @kind, @domain_ref, @redacted_payload, @content_hash, @created_at_utc);
            """, lease.Connection, lease.Transaction);

        AddArtifactParameters(command, artifact);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReplaceJobLevelAsync(TriageArtifact artifact, CancellationToken cancellationToken)
    {
        if (artifact.Attempt is not null)
        {
            throw new ArgumentException("Only job-level artifacts can be replaced.", nameof(artifact));
        }

        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            DELETE FROM incidentcompass.triage_artifacts
            WHERE job_id = @job_id
              AND attempt IS NULL
              AND kind = @kind;

            INSERT INTO incidentcompass.triage_artifacts (
                id, job_id, attempt, kind, domain_ref, redacted_payload, content_hash, created_at_utc)
            VALUES (
                @id, @job_id, @attempt, @kind, @domain_ref, @redacted_payload, @content_hash, @created_at_utc);
            """, lease.Connection, lease.Transaction);

        AddArtifactParameters(command, artifact);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddArtifactParameters(NpgsqlCommand command, TriageArtifact artifact)
    {
        AddParameter(command, "id", artifact.Id);
        AddParameter(command, "job_id", artifact.JobId);
        AddParameter(command, "attempt", artifact.Attempt);
        AddParameter(command, "kind", artifact.Kind.ToString());
        AddParameter(command, "domain_ref", artifact.DomainRef);
        AddJsonParameter(command, "redacted_payload", artifact.RedactedPayload.GetRawText());
        AddParameter(command, "content_hash", artifact.ContentHash);
        AddParameter(command, "created_at_utc", artifact.CreatedAtUtc);
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    private static void AddJsonParameter(NpgsqlCommand command, string name, string value)
    {
        command.Parameters.AddWithValue(name, NpgsqlDbType.Jsonb, value);
    }
}