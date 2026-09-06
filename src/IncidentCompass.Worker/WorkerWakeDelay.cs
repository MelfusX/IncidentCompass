namespace IncidentCompass.Worker;

/// <summary>
/// Waits until either the poll delay elapses or one of the running tasks completes, whichever
/// happens first. The delay runs on a wait-scoped token that is cancelled once the wait is over,
/// and the cancelled delay is then awaited: an abandoned <see cref="Task.Delay(TimeSpan, CancellationToken)"/>
/// faults with <see cref="TaskCanceledException"/>, and an unobserved faulted task would surface
/// later as a process-level <c>UnobservedTaskException</c>.
/// </summary>
internal static class WorkerWakeDelay
{
    public static async Task WaitAsync(
        TimeSpan delay,
        IEnumerable<Task> runningTasks,
        CancellationToken cancellationToken)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(delay, wait.Token);
        var completionTask = Task.WhenAny(runningTasks);
        await Task.WhenAny(delayTask, completionTask);
        await wait.CancelAsync();
        await ObserveDelayAsync(delayTask);
    }

    private static async Task ObserveDelayAsync(Task delayTask)
    {
        try
        {
            await delayTask;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
