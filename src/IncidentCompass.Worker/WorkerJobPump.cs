using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.Worker;

public sealed class WorkerJobPump(
    IServiceScopeFactory serviceScopeFactory,
    ILogger<WorkerJobPump> logger)
{
    private readonly List<Task> activeJobs = [];

    public int ActiveJobCount => activeJobs.Count;

    public async Task ObserveCompletedAsync(CancellationToken cancellationToken)
    {
        for (var index = activeJobs.Count - 1; index >= 0; index--)
        {
            var task = activeJobs[index];
            if (!task.IsCompleted)
            {
                continue;
            }

            activeJobs.RemoveAt(index);
            try
            {
                await task;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Claimed triage job processing failed after claim.");
            }
        }
    }

    public async Task<int> FillAvailableSlotsAsync(
        string workerId,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        var started = 0;
        while (activeJobs.Count < options.MaxConcurrentJobs)
        {
            var job = await ClaimNextAsync(workerId, options, cancellationToken);
            if (job is null)
            {
                break;
            }

            activeJobs.Add(ProcessClaimedAsync(workerId, options, job, cancellationToken));
            started++;
        }

        return started;
    }

    public async Task WaitForNextWakeAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (activeJobs.Count == 0)
        {
            await Task.Delay(delay, cancellationToken);
            return;
        }

        var delayTask = Task.Delay(delay, cancellationToken);
        var completionTask = Task.WhenAny(activeJobs);
        await Task.WhenAny(delayTask, completionTask);
    }

    private async Task<TriageJob?> ClaimNextAsync(
        string workerId,
        WorkerOptions options,
        CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITriageJobRunner>();
        return await runner.ClaimNextAsync(
            workerId,
            TimeSpan.FromSeconds(options.LeaseSeconds),
            cancellationToken);
    }

    private async Task ProcessClaimedAsync(
        string workerId,
        WorkerOptions options,
        TriageJob job,
        CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<ITriageJobRunner>();
        await runner.ProcessClaimedAsync(
            job,
            workerId,
            new TriageJobProcessingSettings(
                options.MaxAttempts,
                TimeSpan.FromSeconds(options.RetryDelaySeconds)),
            cancellationToken);
    }
}
