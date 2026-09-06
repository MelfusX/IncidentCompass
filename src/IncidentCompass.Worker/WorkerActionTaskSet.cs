namespace IncidentCompass.Worker;

internal sealed partial class WorkerActionTaskSet(ILogger<WorkerActionPump> logger)
{
    private readonly List<(Task DispatchTask, CancellationTokenSource Cancellation)> actions = [];

    public int Count => actions.Count;

    public void Add(Task dispatchTask, CancellationTokenSource cancellation) =>
        actions.Add((dispatchTask, cancellation));

    public async Task ObserveCompletedAsync(CancellationToken cancellationToken)
    {
        for (var index = actions.Count - 1; index >= 0; index--)
        {
            var action = actions[index];
            if (!action.DispatchTask.IsCompleted)
            {
                continue;
            }

            actions.RemoveAt(index);
            await ObserveAsync(action, LogDispatchFailedAfterClaim, cancellationToken);
        }
    }

    public async Task DrainAsync()
    {
        var running = actions.ToArray();
        actions.Clear();
        foreach (var action in running)
        {
            action.Cancellation.Cancel();
        }

        foreach (var action in running)
        {
            await ObserveAsync(action, LogDispatchFailedWhileDraining, CancellationToken.None);
        }
    }

    public async Task WaitForNextWakeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (actions.Count == 0)
        {
            await Task.Delay(delay, cancellationToken);
            return;
        }

        var delayTask = Task.Delay(delay, cancellationToken);
        var completionTask = Task.WhenAny(actions.Select(static action => action.DispatchTask));
        await Task.WhenAny(delayTask, completionTask);
    }

    private async Task ObserveAsync(
        (Task DispatchTask, CancellationTokenSource Cancellation) action,
        Action<ILogger, Exception> logFailure,
        CancellationToken cancellationToken)
    {
        try
        {
            await action.DispatchTask;
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
            action.Cancellation.Dispose();
        }
    }

    [LoggerMessage(EventId = 1501, Level = LogLevel.Warning, Message = "Approved action dispatch failed after claim.")]
    private static partial void LogDispatchFailedAfterClaim(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 1502, Level = LogLevel.Warning, Message = "Approved action dispatch failed while draining.")]
    private static partial void LogDispatchFailedWhileDraining(ILogger logger, Exception exception);
}
