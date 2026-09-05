using IncidentCompass.Application.Investigation.Reports.Context;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Investigation;

internal sealed class PostgresReadOnlyContextOutcomeRepository(
    PostgresDataSourceProvider dataSourceProvider) : IReadOnlyContextOutcomeRepository
{
    public Task<IReadOnlyList<ReadOnlyContextOutcome>> ReadCurrentAttemptAsync(
        Guid jobId,
        int attempt,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "read current-attempt context outcomes",
            () => ReadAsync(jobId, attempt, cancellationToken));

    private async Task<IReadOnlyList<ReadOnlyContextOutcome>> ReadAsync(
        Guid jobId,
        int attempt,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT substring(domain_ref from 6) AS tool_name,
                   redacted_payload->>'outcome' AS outcome,
                   redacted_payload->>'code' AS code
            FROM incidentcompass.triage_artifacts
            WHERE job_id = @job_id
              AND attempt = @attempt
              AND kind = 'ToolResult'
              AND domain_ref IN ('tool:source_lookup', 'tool:ticket_search')
              AND redacted_payload->>'outcome' IN ('no_match', 'connector_unavailable')
            ORDER BY created_at_utc, id
            LIMIT 100;
            """, connection);
        command.AddParameter("job_id", jobId);
        command.AddParameter("attempt", attempt);

        var outcomes = new List<ReadOnlyContextOutcome>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var toolName = reader.GetString(0);
            var status = reader.GetString(1) == "no_match"
                ? ReadOnlyContextOutcomeStatus.NoMatch
                : ReadOnlyContextOutcomeStatus.ConnectorUnavailable;
            if (!reader.IsDBNull(2))
            {
                outcomes.Add(new ReadOnlyContextOutcome(toolName, status, reader.GetString(2)));
            }
        }

        return outcomes;
    }
}
