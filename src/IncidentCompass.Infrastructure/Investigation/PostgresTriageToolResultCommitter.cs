using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresTriageToolResultCommitter(
    PostgresDataSourceProvider dataSourceProvider,
    ITriageToolResultCommitFaultInjector faultInjector,
    TimeProvider timeProvider) : ITriageToolResultCommitter
{
    public async Task<TriageArtifact> CommitSucceededAsync(
        TriageToolResultCommitRequest request,
        CancellationToken cancellationToken)
    {
        var createdAtUtc = timeProvider.GetUtcNow();
        var artifact = new TriageArtifact(
            Guid.NewGuid(),
            request.Job.Id,
            request.Job.Attempt,
            ArtifactKind.ToolResult,
            "tool:" + request.ToolName,
            request.Output.Clone(),
            request.ContentHash,
            createdAtUtc);

        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            foreach (var additionalArtifact in request.AdditionalArtifacts ?? [])
            {
                ValidateAdditionalArtifact(request, additionalArtifact);
                await InsertArtifactAsync(connection, transaction, additionalArtifact, cancellationToken);
            }

            await InsertArtifactAsync(connection, transaction, artifact, cancellationToken);
            await faultInjector.AfterArtifactInsertedAsync(cancellationToken);
            await InsertToolResultEventAsync(connection, transaction, request, artifact, createdAtUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return artifact;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static void ValidateAdditionalArtifact(
        TriageToolResultCommitRequest request,
        TriageArtifact artifact)
    {
        if (artifact.JobId != request.Job.Id || artifact.Attempt != request.Job.Attempt)
        {
            throw new InvalidOperationException("Additional tool artifacts must belong to the current job attempt.");
        }

        if (artifact.Kind == ArtifactKind.ToolResult)
        {
            throw new InvalidOperationException("Additional tool artifacts must not use ToolResult kind.");
        }
    }

    private static async Task InsertArtifactAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_artifacts (
                id, job_id, attempt, kind, domain_ref, redacted_payload, content_hash, created_at_utc)
            VALUES (
                @id, @job_id, @attempt, @kind, @domain_ref, @redacted_payload, @content_hash, @created_at_utc);
            """, connection, transaction);

        AddParameter(command, "id", artifact.Id);
        AddParameter(command, "job_id", artifact.JobId);
        AddParameter(command, "attempt", artifact.Attempt);
        AddParameter(command, "kind", artifact.Kind.ToString());
        AddParameter(command, "domain_ref", artifact.DomainRef);
        AddJsonParameter(command, "redacted_payload", artifact.RedactedPayload.GetRawText());
        AddParameter(command, "content_hash", artifact.ContentHash);
        AddParameter(command, "created_at_utc", artifact.CreatedAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertToolResultEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageToolResultCommitRequest request,
        TriageArtifact artifact,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, role, tool_name, rationale,
                decision, decision_reason, tool_status, payload_ref, config_hash, created_at_utc)
            VALUES (
                @fault_id, @job_id, @attempt, 'ToolResult', @role, @tool_name, @rationale,
                NULL, NULL, @tool_status, @payload_ref, @config_hash, @created_at_utc);
            """, connection, transaction);

        AddParameter(command, "fault_id", request.Job.FaultId);
        AddParameter(command, "job_id", request.Job.Id);
        AddParameter(command, "attempt", request.Job.Attempt);
        AddParameter(command, "role", request.Role);
        AddParameter(command, "tool_name", request.ToolName);
        AddParameter(command, "rationale", request.Rationale);
        AddParameter(command, "tool_status", TriageLedgerToolStatus.Succeeded.ToString());
        AddParameter(command, "payload_ref", "artifact:" + artifact.Id);
        AddParameter(command, "config_hash", request.Job.ConfigHash);
        AddParameter(command, "created_at_utc", createdAtUtc);
        await command.ExecuteNonQueryAsync(cancellationToken);
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