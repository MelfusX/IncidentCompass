using IncidentCompass.Application.Governance.Ledger;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Domain.Incidents.Statuses;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance;

internal sealed class PostgresTriageLedgerReader(PostgresDataSourceProvider dataSourceProvider) : ITriageLedgerReader
{
    public async Task<TriageBudgetLedgerUsage> ReadBudgetUsageAsync(
        TriageJob job,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT COALESCE(SUM(tokens_delta), 0), COALESCE(SUM(workers_delta), 0)
            FROM incidentcompass.triage_ledger
            WHERE event_type = 'BudgetEvent'
            """ + ScopePredicate("attempt") + ";",
            connection);
        AddScopeParameters(command, job, "attempt");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new TriageBudgetLedgerUsage(TokensSpent: 0, WorkerCalls: 0);
        }

        return new TriageBudgetLedgerUsage(Convert.ToInt32(reader.GetInt64(0)), Convert.ToInt32(reader.GetInt64(1)));
    }

    public async Task<int> CountPolicyDecisionsAsync(
        TriageJob job,
        string toolName,
        string scope,
        TriageLedgerDecision decision,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)
            FROM incidentcompass.triage_ledger
            WHERE event_type = 'PolicyDecision'
              AND tool_name = @tool_name
              AND decision = @decision
            """ + ScopePredicate(scope),
            connection);
        AddScopeParameters(command, job, scope);
        command.AddParameter("tool_name", toolName);
        command.AddParameter("decision", decision.ToDbString());

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<bool> HasSuccessfulToolResultAsync(
        TriageJob job,
        string toolName,
        string scope,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.triage_ledger
                WHERE event_type = 'ToolResult'
                  AND tool_name = @tool_name
                  AND tool_status = @tool_status
            """ + ScopePredicate(scope) + ");",
            connection);
        AddScopeParameters(command, job, scope);
        command.AddParameter("tool_name", toolName);
        command.AddParameter("tool_status", TriageLedgerToolStatus.Succeeded.ToDbString());

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }


    public async Task<IReadOnlyList<TriageLedgerEntry>> ReadByFaultIdAsync(
        Guid faultId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, fault_id, job_id, attempt, event_type, role, tool_name, rationale,
                   decision, decision_reason, payload_ref, config_hash, created_at_utc,
                   tool_status, tokens_delta, workers_delta
            FROM incidentcompass.triage_ledger
            WHERE fault_id = @fault_id
            ORDER BY id;
            """,
            connection);
        command.AddParameter("fault_id", faultId);

        var entries = new List<TriageLedgerEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new TriageLedgerEntry(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetGuid(2),
                reader.GetInt32(3),
                Enum.Parse<TriageLedgerEventType>(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : Enum.Parse<TriageLedgerDecision>(reader.GetString(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetString(11),
                reader.GetDateTimeOffset(12),
                reader.IsDBNull(13) ? null : Enum.Parse<TriageLedgerToolStatus>(reader.GetString(13)),
                reader.IsDBNull(14) ? null : reader.GetInt32(14),
                reader.IsDBNull(15) ? null : reader.GetInt32(15)));
        }

        return entries;
    }
    private static string ScopePredicate(string scope)
    {
        return scope switch
        {
            "attempt" => " AND job_id = @job_id AND attempt = @attempt",
            "job" => " AND job_id = @job_id",
            "fault" => " AND fault_id = @fault_id",
            _ => " AND job_id = @job_id AND attempt = @attempt"
        };
    }


    private static void AddScopeParameters(NpgsqlCommand command, TriageJob job, string scope)
    {
        if (scope == "fault")
        {
            command.AddParameter("fault_id", job.FaultId);
            return;
        }

        command.AddParameter("job_id", job.Id);
        if (scope != "job")
        {
            command.AddParameter("attempt", job.Attempt);
        }
    }
}
