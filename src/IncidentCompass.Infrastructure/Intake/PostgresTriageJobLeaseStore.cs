using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresTriageJobLeaseStore(
    PostgresDataSourceProvider dataSourceProvider,
    TimeProvider timeProvider)
{
    public Task<bool> RenewAsync(
        TriageJob job,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "renew triage job lease",
            () => RenewCoreAsync(job, workerId, leaseDuration, cancellationToken));

    private async Task<bool> RenewCoreAsync(
        TriageJob job,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.triage_jobs
            SET locked_until_utc = @locked_until_utc,
                updated_at_utc = @now
            WHERE id = @job_id
              AND status = 'Processing'
              AND attempt = @attempt
              AND locked_by = @worker_id
              AND locked_until_utc > @now;
            """, connection);
        command.AddParameter("locked_until_utc", now.Add(leaseDuration));
        command.AddParameter("now", now);
        command.AddParameter("job_id", job.Id);
        command.AddParameter("attempt", job.Attempt);
        command.AddParameter("worker_id", workerId);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }
}
