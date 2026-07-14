using Microsoft.Extensions.Logging;

namespace IncidentCompass.Worker;

internal sealed class WorkerJobTaskSet(ILogger<WorkerJobPump> logger)
{
    private readonly List<(Task ProcessingTask, CancellationTokenSource Cancellation)> jobs = [];

    public int Count => jobs.Count;

    public void Add(Task processingTask, CancellationTokenSource cancellation) => jobs.Add((processingTask, cancellation));

    public async Task ObserveCompletedAsync(CancellationToken cancellationToken)
    {
        for (var index = jobs.Count - 1; index >= 0; index--)
        {
            var job = jobs[index];
            if (!job.ProcessingTask.IsCompleted)
            {
                continue;
            }

            jobs.RemoveAt(index);
            try
            {
                await job.ProcessingTask;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Claimed triage job processing failed after claim.");
            }
            finally
            {
                job.Cancellation.Dispose();
            }
        }
    }

    public async Task DrainAsync()
    {
        var runningJobs = jobs.ToArray();
        jobs.Clear();
        foreach (var job in runningJobs)
        {
            job.Cancellation.Cancel();
        }

        foreach (var job in runningJobs)
        {
            try
            {
                await job.ProcessingTask;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Claimed triage job processing failed while draining the worker.");
            }
            finally
            {
                job.Cancellation.Dispose();
            }
        }
    }

    public async Task WaitForNextWakeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (jobs.Count == 0)
        {
            await Task.Delay(delay, cancellationToken);
            return;
        }

        var delayTask = Task.Delay(delay, cancellationToken);
        var completionTask = Task.WhenAny(jobs.Select(static job => job.ProcessingTask));
        await Task.WhenAny(delayTask, completionTask);
    }
}