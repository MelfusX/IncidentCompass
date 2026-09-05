namespace IncidentCompass.Worker;

internal sealed class WorkerActionTaskSet(ILogger<WorkerActionPump> logger)
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
            await ObserveAsync(action, cancellationToken, "Approved action dispatch failed after claim.");
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
            await ObserveAsync(action, CancellationToken.None, "Approved action dispatch failed while draining.");
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
        CancellationToken cancellationToken,
        string failureMessage)
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
            logger.LogWarning(exception, failureMessage);
        }
        finally
        {
            action.Cancellation.Dispose();
        }
    }
}
