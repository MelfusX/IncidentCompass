using System.Globalization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed class PostgresActionToolRuleFactReader(
    NpgsqlConnection connection,
    NpgsqlTransaction transaction,
    GroundedActionProposalContext origin) : IToolRuleFactReader
{
    public Task<int> CountAcceptedUsesAsync(
        string toolName,
        string scope,
        CancellationToken cancellationToken) =>
        CountAsync("ActionProposed", toolName, scope, cancellationToken);

    public async Task<bool> HasSuccessfulToolResultAsync(
        string toolName,
        string scope,
        CancellationToken cancellationToken)
    {
        var attemptPredicate = ScopePredicate(scope);
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1
                FROM incidentcompass.triage_ledger
                WHERE event_type = 'ToolResult'
                  AND tool_status = 'Succeeded'
                  AND job_id = @job_id
                  AND tool_name = @tool_name
            """ + attemptPredicate + ");", connection, transaction);
        AddParameters(command, toolName, attemptPredicate);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<int> CountAsync(
        string eventType,
        string toolName,
        string scope,
        CancellationToken cancellationToken)
    {
        var attemptPredicate = ScopePredicate(scope);
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM incidentcompass.triage_ledger
            WHERE event_type = @event_type
              AND job_id = @job_id
              AND tool_name = @tool_name
            """ + attemptPredicate + ";", connection, transaction);
        command.AddParameter("event_type", eventType);
        AddParameters(command, toolName, attemptPredicate);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private void AddParameters(NpgsqlCommand command, string toolName, string attemptPredicate)
    {
        command.AddParameter("job_id", origin.JobId);
        command.AddParameter("tool_name", toolName);
        if (attemptPredicate.Length > 0)
        {
            command.AddParameter("attempt", origin.Attempt);
        }
    }

    private static string ScopePredicate(string scope) =>
        string.Equals(scope, "attempt", StringComparison.Ordinal)
            ? " AND attempt = @attempt"
            : string.Empty;
}
