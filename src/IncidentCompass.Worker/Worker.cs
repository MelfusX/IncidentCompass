using IncidentCompass.Application.Core.Dispatching;
using IncidentCompass.Application.Core.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.Worker;

// Placeholder background loop. Phase 2 replaces this with the triage job-claim loop;
// for now it only performs a startup health check and idles between polls.
public sealed partial class Worker(
    ILogger<Worker> logger,
    IServiceScopeFactory serviceScopeFactory)
    : BackgroundService
{
    private const int PollIntervalSeconds = 30;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await TryLogStartupStatusAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = WorkerPollDelay.Calculate(PollIntervalSeconds, consecutiveErrors: 0, Random.Shared.NextDouble());
            await Task.Delay(delay, stoppingToken);
        }
    }

    private async Task TryLogStartupStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = serviceScopeFactory.CreateScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IApplicationDispatcher>();

            var health = await dispatcher.DispatchAsync<GetHealthStatusQuery, HealthStatus>(
                new GetHealthStatusQuery("worker"),
                cancellationToken);

            LogWorkerStarted(
                logger,
                health.Status,
                health.CheckedAtUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogStartupHealthCheckFailed(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "IncidentCompass Worker started with application status {Status} at {CheckedAtUtc}")]
    private static partial void LogWorkerStarted(
        ILogger logger,
        string status,
        DateTimeOffset checkedAtUtc);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Warning,
        Message = "Worker startup health check failed. Polling will continue.")]
    private static partial void LogStartupHealthCheckFailed(
        ILogger logger,
        Exception exception);
}
