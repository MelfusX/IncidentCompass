using Microsoft.Extensions.Logging;

namespace IncidentCompass.Worker;

internal sealed class PostReportActionEvaluationTaskSet(
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
            await ObserveAsync(evaluation, cancellationToken,
                "Post-report action evaluation failed after claim.");
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
            await ObserveAsync(evaluation, CancellationToken.None,
                "Post-report action evaluation failed while draining.");
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
        CancellationToken cancellationToken,
        string failureMessage)
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
            logger.LogWarning(exception, failureMessage);
        }
        finally
        {
            evaluation.Cancellation.Dispose();
        }
    }
}
