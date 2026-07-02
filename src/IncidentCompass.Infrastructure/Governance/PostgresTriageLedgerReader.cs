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
        command.Parameters.AddWithValue("tool_name", toolName);
        command.Parameters.AddWithValue("decision", decision.ToString());

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
        command.Parameters.AddWithValue("tool_name", toolName);
        command.Parameters.AddWithValue("tool_status", TriageLedgerToolStatus.Succeeded.ToString());

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
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
            command.Parameters.AddWithValue("fault_id", job.FaultId);
            return;
        }

        command.Parameters.AddWithValue("job_id", job.Id);
        if (scope != "job")
        {
            command.Parameters.AddWithValue("attempt", job.Attempt);
        }
    }
}