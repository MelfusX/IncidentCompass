using Microsoft.Extensions.Hosting;

namespace IncidentCompass.Infrastructure.Postgres;

internal sealed class PostgresMigrationHostedService(
    PostgresMigrationRunner migrationRunner,
    PostgresMigrationReadiness readiness) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await migrationRunner.MigrateAsync(cancellationToken);
            readiness.MarkReady();
        }
        catch
        {
            readiness.MarkFailed();
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}