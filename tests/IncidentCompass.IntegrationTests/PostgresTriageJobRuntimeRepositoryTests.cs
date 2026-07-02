using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IncidentCompass.IntegrationTests;

[Collection(PostgresRepositoryCollection.CollectionName)]
public sealed class PostgresTriageJobRuntimeRepositoryTests(PostgresRepositoryFixture postgres)
{
    [DockerAvailableFact]
    public async Task ClaimNextAsync_PendingJob_ClaimsAndMarksFaultAnalyzing()
    {
        using var scope = await CreateScopeAsync();
        var seed = await SeedJobAsync(scope.ConnectionString, "claim-pending", "Pending");
        var repository = scope.Services.GetRequiredService<ITriageJobRuntimeRepository>();

        var claimed = await repository.ClaimNextAsync(
            "worker-a",
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.NotNull(claimed);
        Assert.Equal(seed.JobId, claimed.Id);
        Assert.Equal(TriageJobStatus.Processing, claimed.Status);
        Assert.Equal(1, claimed.Attempt);
        Assert.Equal("worker-a", claimed.LockedBy);
        Assert.Equal(seed.ConfigHash, claimed.ConfigHash);
        Assert.NotNull(claimed.LockedUntilUtc);

        var faultStatus = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT status FROM incidentcompass.faults WHERE id = @fault_id;",
            ("fault_id", seed.FaultId));
        Assert.Equal("Analyzing", faultStatus);
    }

    [DockerAvailableFact]
    public async Task ClaimNextAsync_ExpiredProcessingJob_ReclaimsAsNextAttempt()
    {
        using var scope = await CreateScopeAsync();
        var seed = await SeedJobAsync(scope.ConnectionString, "claim-expired", "Processing");
        await ExecuteAsync(
            scope.ConnectionString,
            """
            UPDATE incidentcompass.triage_jobs
            SET locked_by = 'old-worker', locked_until_utc = now() - interval '1 minute'
            WHERE id = @job_id;
            """,
            ("job_id", seed.JobId));
        var repository = scope.Services.GetRequiredService<ITriageJobRuntimeRepository>();

        var claimed = await repository.ClaimNextAsync(
            "worker-b",
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.NotNull(claimed);
        Assert.Equal(seed.JobId, claimed.Id);
        Assert.Equal(2, claimed.Attempt);
        Assert.Equal("worker-b", claimed.LockedBy);
    }

    [DockerAvailableFact]
    public async Task ClaimNextAsync_RetryPendingHonorsNextAttemptTime()
    {
        using var scope = await CreateScopeAsync();
        var seed = await SeedJobAsync(scope.ConnectionString, "claim-retry", "RetryPending");
        await ExecuteAsync(
            scope.ConnectionString,
            """
            UPDATE incidentcompass.triage_jobs
            SET next_attempt_at_utc = now() + interval '1 hour'
            WHERE id = @job_id;
            """,
            ("job_id", seed.JobId));
        var repository = scope.Services.GetRequiredService<ITriageJobRuntimeRepository>();

        var notDue = await repository.ClaimNextAsync(
            "worker-c",
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);
        Assert.Null(notDue);

        await ExecuteAsync(
            scope.ConnectionString,
            """
            UPDATE incidentcompass.triage_jobs
            SET next_attempt_at_utc = now() - interval '1 minute'
            WHERE id = @job_id;
            """,
            ("job_id", seed.JobId));

        var claimed = await repository.ClaimNextAsync(
            "worker-c",
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);

        Assert.NotNull(claimed);
        Assert.Equal(2, claimed.Attempt);
        Assert.Equal("worker-c", claimed.LockedBy);
    }

    [DockerAvailableFact]
    public async Task RecordAttemptFailureAsync_RetryAndDeadLetterTransitionsAreFenced()
    {
        using var scope = await CreateScopeAsync();
        var seed = await SeedJobAsync(scope.ConnectionString, "claim-failure", "Pending");
        var repository = scope.Services.GetRequiredService<ITriageJobRuntimeRepository>();
        var claimed = await repository.ClaimNextAsync(
            "worker-d",
            TimeSpan.FromMinutes(5),
            TestContext.Current.CancellationToken);
        Assert.NotNull(claimed);

        await repository.RecordAttemptFailureAsync(
            claimed,
            "worker-d",
            new TriageJobAttemptFailure(
                TriageJobStatus.RetryPending,
                "test_retry",
                "retry this attempt",
                DateTimeOffset.UtcNow.AddMinutes(1)),
            TestContext.Current.CancellationToken);

        var retryRow = await ReadJobStateAsync(scope.ConnectionString, seed.JobId);
        Assert.Equal("RetryPending", retryRow.Status);
        Assert.Null(retryRow.LockedBy);
        Assert.NotNull(retryRow.NextAttemptAtUtc);
        Assert.Equal("test_retry", retryRow.LastErrorCode);

        await ExecuteAsync(
            scope.ConnectionString,
            """
            UPDATE incidentcompass.triage_jobs
            SET status = 'Processing', attempt = 2, locked_by = 'worker-d', locked_until_utc = now() + interval '5 minutes'
            WHERE id = @job_id;
            """,
            ("job_id", seed.JobId));
        var secondAttempt = claimed with { Attempt = 2, LockedBy = "worker-d" };

        await repository.RecordAttemptFailureAsync(
            secondAttempt,
            "worker-d",
            new TriageJobAttemptFailure(
                TriageJobStatus.DeadLettered,
                "test_deadletter",
                "dead letter this job",
                NextAttemptAtUtc: null),
            TestContext.Current.CancellationToken);

        var deadLetteredRow = await ReadJobStateAsync(scope.ConnectionString, seed.JobId);
        Assert.Equal("DeadLettered", deadLetteredRow.Status);
        Assert.Null(deadLetteredRow.LockedBy);
        Assert.Null(deadLetteredRow.NextAttemptAtUtc);
        Assert.Equal("test_deadletter", deadLetteredRow.LastErrorCode);

        var faultStatus = await ScalarAsync<string>(
            scope.ConnectionString,
            "SELECT status FROM incidentcompass.faults WHERE id = @fault_id;",
            ("fault_id", seed.FaultId));
        Assert.Equal("Failed", faultStatus);
    }

    private async Task<RepositoryScope> CreateScopeAsync()
    {
        var connectionString = await postgres.GetConnectionStringAsync();
        await PostgresSchemaTestHelper.EnsureSchemaAsync(connectionString);
        await PostgresTriageJobTestIsolation.CompleteClaimableJobsAsync(connectionString);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:IncidentCompass"] = connectionString,
                ["IncidentCompass:Postgres:ConnectionStringName"] = "IncidentCompass"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTestApplication(configuration);
        services.AddInfrastructure(configuration);
        var serviceProvider = services.BuildServiceProvider();

        return new RepositoryScope(serviceProvider, connectionString);
    }

    private static async Task<JobSeed> SeedJobAsync(string connectionString, string prefix, string jobStatus)
    {
        var signalId = Guid.NewGuid();
        var faultId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var unique = Guid.NewGuid().ToString("N");
        var fingerprint = prefix + "-fingerprint-" + unique;
        var serviceName = prefix + "-service-" + unique;
        var configHash = prefix + "-config-" + unique;
        var faultStatus = jobStatus == "Processing" ? "Analyzing" : "Queued";

        await ExecuteAsync(
            connectionString,
            """
            INSERT INTO incidentcompass.triage_config_snapshots (config_hash, serialized_config, instructions, created_at_utc)
            VALUES (@config_hash, '{}'::jsonb, '{}'::jsonb, now());

            INSERT INTO incidentcompass.signals (
                id, tenant_id, source, fault_id, fingerprint, fingerprint_version, fingerprint_strength,
                external_id, is_suppressed, suppressed_by_fault_id, suppression_reason, trace_id, span_id,
                parent_span_id, service_name, environment, operation_name, severity, error_type, error_message,
                summary, description, http_method, http_route, http_status_code, duration_ms, attributes, body,
                observed_at_utc, received_at_utc)
            VALUES (
                @signal_id, 'local', 'tester', NULL, @fingerprint, 1, 'strong',
                NULL, false, NULL, NULL, NULL, NULL, NULL, @service_name, 'prod', 'POST /claim',
                'warning', 'ClaimProbe', 'claim probe', 'Claim probe', NULL, 'POST', '/claim',
                500, 1000, '{}'::jsonb, '{}'::jsonb, now(), now());

            INSERT INTO incidentcompass.faults (
                id, trigger_signal_id, tenant_id, status, fingerprint, fingerprint_version,
                fingerprint_strength, service_name, environment, severity, correlation_id,
                created_at_utc, completed_at_utc, recurrence_of)
            VALUES (
                @fault_id, @signal_id, 'local', @fault_status, @fingerprint, 1,
                'strong', @service_name, 'prod', 'warning', NULL, now(), NULL, NULL);

            UPDATE incidentcompass.signals
            SET fault_id = @fault_id
            WHERE id = @signal_id;

            INSERT INTO incidentcompass.triage_jobs (
                id, fault_id, status, attempt, locked_by, locked_until_utc, next_attempt_at_utc,
                last_error_code, last_error_message, config_hash, created_at_utc, updated_at_utc)
            VALUES (
                @job_id, @fault_id, @job_status, 1, NULL, NULL, NULL,
                NULL, NULL, @config_hash, now(), now());
            """,
            ("config_hash", configHash),
            ("signal_id", signalId),
            ("fault_id", faultId),
            ("job_id", jobId),
            ("fingerprint", fingerprint),
            ("service_name", serviceName),
            ("fault_status", faultStatus),
            ("job_status", jobStatus));

        return new JobSeed(faultId, jobId, configHash);
    }

    private static async Task<JobStateRow> ReadJobStateAsync(string connectionString, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT status, locked_by, next_attempt_at_utc, last_error_code
            FROM incidentcompass.triage_jobs
            WHERE id = @job_id;
            """,
            connection);
        command.Parameters.AddWithValue("job_id", jobId);

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            throw new InvalidOperationException("Seeded triage job was not found.");
        }

        return new JobStateRow(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDateTime(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task ExecuteAsync(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return (T)result!;
    }

    private sealed record RepositoryScope(ServiceProvider Services, string ConnectionString) : IDisposable
    {
        public void Dispose()
        {
            Services.Dispose();
        }
    }

    private sealed record JobSeed(Guid FaultId, Guid JobId, string ConfigHash);

    private sealed record JobStateRow(string Status, string? LockedBy, DateTime? NextAttemptAtUtc, string? LastErrorCode);
}
