using IncidentCompass.Application.Governance.PostReportActions;

namespace IncidentCompass.Worker;

public sealed partial class PostReportActionEvaluationLeaseRenewer(
    ILogger<PostReportActionEvaluationLeaseRenewer> logger)
{
    private static readonly TimeSpan MinimumRenewalInterval = TimeSpan.FromMilliseconds(100);

    public async Task<bool> RenewUntilStoppedAsync(
        IPostReportActionIntentRepository repository,
        PostReportActionIntentClaim claim,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromTicks(leaseDuration.Ticks / 3);
        if (interval < MinimumRenewalInterval)
        {
            interval = MinimumRenewalInterval;
        }

        while (true)
        {
            await Task.Delay(interval, cancellationToken);
            if (!await repository.RenewLeaseAsync(
                    claim.Intent.Id, workerId, claim.Fence, leaseDuration, cancellationToken))
            {
                return false;
            }
        }
    }

    public async Task<PostReportActionWorkflowResult?> EvaluateAsync(
        IPostReportActionIntentRepository repository,
        IPostReportActionWorkflow workflow,
        PostReportActionIntentClaim claim,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        using var processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var workflowTask = workflow.EvaluateAsync(claim.Intent, processing.Token);
        var renewalTask = RenewUntilStoppedAsync(
            repository, claim, workerId, leaseDuration, processing.Token);
        var first = await Task.WhenAny(workflowTask, renewalTask);
        if (first == renewalTask)
        {
            try
            {
                var retained = await renewalTask;
                return retained ? await workflowTask : null;
            }
            finally
            {
                processing.Cancel();
                await ObserveAbandonedWorkflowAsync(workflowTask);
            }
        }

        try
        {
            return await workflowTask;
        }
        finally
        {
            processing.Cancel();
            await ObserveCancellationAsync(renewalTask);
        }
    }

    private async Task ObserveAbandonedWorkflowAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            LogWorkflowFailedAfterLeaseLost(logger, exception);
        }
    }

    [LoggerMessage(EventId = 1701, Level = LogLevel.Warning, Message = "Post-report workflow failed after its evaluation lease was lost.")]
    private static partial void LogWorkflowFailedAfterLeaseLost(ILogger logger, Exception exception);

    private static async Task ObserveCancellationAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
