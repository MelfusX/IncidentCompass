using Npgsql;

namespace IncidentCompass.IntegrationTests;

internal static class PostgresTriageJobTestIsolation
{
    public static async Task CompleteClaimableJobsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            WITH swept_jobs AS (
                UPDATE incidentcompass.triage_jobs
                SET status = 'DeadLettered',
                    locked_by = NULL,
                    locked_until_utc = NULL,
                    next_attempt_at_utc = NULL,
                    last_error_code = COALESCE(last_error_code, 'test_isolation_sweep'),
                    last_error_message = COALESCE(last_error_message, 'Claimable job completed by test isolation sweep.'),
                    updated_at_utc = now()
                WHERE status IN ('Pending', 'RetryPending', 'Processing')
                RETURNING fault_id
            )
            UPDATE incidentcompass.faults
            SET status = 'Failed',
                completed_at_utc = COALESCE(completed_at_utc, now())
            WHERE id IN (SELECT fault_id FROM swept_jobs);
            """,
            connection);

        await command.ExecuteNonQueryAsync();
    }
}
