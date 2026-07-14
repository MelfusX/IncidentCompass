using IncidentCompass.Application.Core.Observability;
using Microsoft.Extensions.Hosting;

namespace IncidentCompass.Infrastructure.Postgres;

internal sealed class PostgresMigrationHostedService(
    PostgresMigrationRunner migrationRunner,
    PostgresMigrationReadiness readiness,
    IRuntimeTelemetry? telemetry = null) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var migrationTelemetry = telemetry?.StartMigration();
        try
        {
            await migrationRunner.MigrateAsync(cancellationToken);
            readiness.MarkReady();
            telemetry?.RecordMigration(RuntimeTelemetryOutcome.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            readiness.MarkFailed();
            telemetry?.RecordMigration(RuntimeTelemetryOutcome.Cancelled);
            throw;
        }
        catch
        {
            readiness.MarkFailed();
            telemetry?.RecordMigration(RuntimeTelemetryOutcome.Failed);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}