using Microsoft.Extensions.Options;

namespace IncidentCompass.Worker;

internal sealed partial class PostReportActionEvaluationWorker(
    ILogger<PostReportActionEvaluationWorker> logger,
    IOptions<PostReportActionEvaluationOptions> options,
    PostReportActionEvaluationPump evaluationPump) : BackgroundService
{
    private readonly string workerId = $"{Environment.MachineName}:post-report:{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consecutiveErrors = 0;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await evaluationPump.ObserveCompletedAsync(stoppingToken);
                    await evaluationPump.FillAvailableSlotsAsync(workerId, options.Value, stoppingToken);
                    consecutiveErrors = 0;
                    var delay = WorkerPollDelay.Calculate(
                        options.Value.PollIntervalSeconds,
                        consecutiveErrors,
                        Random.Shared.NextDouble());
                    await evaluationPump.WaitForNextWakeAsync(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    consecutiveErrors++;
                    LogEvaluationPollFailed(logger, exception);
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
            await evaluationPump.DrainAsync();
        }
    }

    [LoggerMessage(
        EventId = 1301,
        Level = LogLevel.Warning,
        Message = "Post-report action evaluation polling failed. Polling will continue after backoff.")]
    private static partial void LogEvaluationPollFailed(ILogger logger, Exception exception);
}
