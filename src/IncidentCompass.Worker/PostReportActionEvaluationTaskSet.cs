namespace IncidentCompass.Worker;

internal sealed partial class PostReportActionEvaluationTaskSet(
    ILogger<PostReportActionEvaluationPump> logger)
{
    private readonly List<(Task Task, CancellationTokenSource Cancellation)> evaluations = [];

    public int Count => evaluations.Count;

    public void Add(Task task, CancellationTokenSource cancellation) =>
        evaluations.Add((task, cancellation));

    public async Task ObserveCompletedAsync(CancellationToken cancellationToken)
    {
        for (var index = evaluations.Count - 1; index >= 0; index--)
        {
            var evaluation = evaluations[index];
            if (!evaluation.Task.IsCompleted)
            {
                continue;
            }

            evaluations.RemoveAt(index);
            await ObserveAsync(evaluation, LogEvaluationFailedAfterClaim, cancellationToken);
        }
    }

    public async Task DrainAsync()
    {
        var running = evaluations.ToArray();
        evaluations.Clear();
        foreach (var evaluation in running)
        {
            evaluation.Cancellation.Cancel();
        }

        foreach (var evaluation in running)
        {
            await ObserveAsync(evaluation, LogEvaluationFailedWhileDraining, CancellationToken.None);
        }
    }

    public async Task WaitForNextWakeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (evaluations.Count == 0)
        {
            await Task.Delay(delay, cancellationToken);
            return;
        }

        var delayTask = Task.Delay(delay, cancellationToken);
        var completionTask = Task.WhenAny(evaluations.Select(static evaluation => evaluation.Task));
        await Task.WhenAny(delayTask, completionTask);
    }

    private async Task ObserveAsync(
        (Task Task, CancellationTokenSource Cancellation) evaluation,
        Action<ILogger, Exception> logFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await evaluation.Task;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            logFailure(logger, exception);
        }
        finally
        {
            evaluation.Cancellation.Dispose();
        }
    }

    [LoggerMessage(EventId = 1601, Level = LogLevel.Warning, Message = "Post-report action evaluation failed after claim.")]
    private static partial void LogEvaluationFailedAfterClaim(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1602, Level = LogLevel.Warning, Message = "Post-report action evaluation failed while draining.")]
    private static partial void LogEvaluationFailedWhileDraining(ILogger logger, Exception exception);
}
