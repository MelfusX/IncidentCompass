using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance;

internal sealed class PostgresTriageLedgerWriter(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider) : ITriageLedgerWriter
{
    public async Task<TriageLedgerEntry> AppendAsync(
        TriageLedgerAppendRequest request,
        CancellationToken cancellationToken)
    {
        var createdAtUtc = timeProvider.GetUtcNow();
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var entry = await InsertAsync(connection, transaction, request, createdAtUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return entry;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<TriageLedgerEntry> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TriageLedgerAppendRequest request,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.triage_ledger (
                fault_id, job_id, attempt, event_type, role, tool_name, rationale,
                decision, decision_reason, tool_status, tokens_delta, workers_delta,
                payload_ref, config_hash, created_at_utc)
            VALUES (
                @fault_id, @job_id, @attempt, @event_type, @role, @tool_name, @rationale,
                @decision, @decision_reason, @tool_status, @tokens_delta, @workers_delta,
                @payload_ref, @config_hash, @created_at_utc)
            RETURNING id;
            """, connection, transaction);

        AddParameter(command, "fault_id", request.FaultId);
        AddParameter(command, "job_id", request.JobId);
        AddParameter(command, "attempt", request.Attempt);
        AddParameter(command, "event_type", request.EventType.ToString());
        AddParameter(command, "role", request.Role);
        AddParameter(command, "tool_name", request.ToolName);
        AddParameter(command, "rationale", request.Rationale);
        AddParameter(command, "decision", request.Decision?.ToString());
        AddParameter(command, "decision_reason", request.DecisionReason);
        AddParameter(command, "tool_status", request.ToolStatus?.ToString());
        AddParameter(command, "tokens_delta", request.TokensDelta);
        AddParameter(command, "workers_delta", request.WorkersDelta);
        AddParameter(command, "payload_ref", request.PayloadRef);
        AddParameter(command, "config_hash", request.ConfigHash);
        AddParameter(command, "created_at_utc", createdAtUtc);

        var id = (long)(await command.ExecuteScalarAsync(cancellationToken))!;

        return new TriageLedgerEntry(
            id,
            request.FaultId,
            request.JobId,
            request.Attempt,
            request.EventType,
            request.Role,
            request.ToolName,
            request.Rationale,
            request.Decision,
            request.DecisionReason,
            request.PayloadRef,
            request.ConfigHash,
            createdAtUtc,
            request.ToolStatus,
            request.TokensDelta,
            request.WorkersDelta);
    }

    private static void AddParameter(NpgsqlCommand command, string name, object? value)
    {
        command.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }
}