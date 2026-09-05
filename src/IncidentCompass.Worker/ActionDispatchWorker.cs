using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IncidentCompass.Worker;

internal sealed partial class ActionDispatchWorker(
    ILogger<ActionDispatchWorker> logger,
    IOptions<ActionDispatchOptions> options,
    WorkerActionPump actionPump) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:actions:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveErrors = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await actionPump.ObserveCompletedAsync(stoppingToken);
                    await actionPump.SweepAsync(options.Value, stoppingToken);
                    await actionPump.FillAvailableSlotsAsync(workerId, options.Value, stoppingToken);
                    consecutiveErrors = 0;
                    var delay = WorkerPollDelay.Calculate(
                        options.Value.PollIntervalSeconds,
                        consecutiveErrors,
                        Random.Shared.NextDouble());
                    await actionPump.WaitForNextWakeAsync(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    consecutiveErrors++;
                    LogActionPollFailed(logger, exception);
                    var delay = WorkerPollDelay.Calculate(
                        options.Value.PollIntervalSeconds,
                        consecutiveErrors,
                        Random.Shared.NextDouble());
                    await Task.Delay(delay, stoppingToken);
                }
            }
        }
        finally
        {
            await actionPump.DrainAsync();
        }
    }

    [LoggerMessage(
        EventId = 1201,
        Level = LogLevel.Warning,
        Message = "Approved action polling failed. Polling will continue after backoff.")]
    private static partial void LogActionPollFailed(ILogger logger, Exception exception);
}
