using IncidentCompass.Infrastructure.Configuration;
using IncidentCompass.Infrastructure.Postgres;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IncidentCompass.Api.Health;

internal sealed class PostgresReadinessHealthCheck(
    IConfiguration configuration,
    IOptions<PostgresOptions> options,
    IPostgresMigrationReadiness migrationReadiness)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!migrationReadiness.IsReady)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL migrations have not completed successfully.");
        }

        var connectionStringName = options.Value.ConnectionStringName;
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return HealthCheckResult.Unhealthy(
                $"PostgreSQL connection string '{connectionStringName}' is not configured.");
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1;", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL is reachable.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.", exception);
        }
    }
}