using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.Infrastructure.Memory;

internal sealed partial class MemorySeedSyncStatusPersistence(
    IServiceScopeFactory scopeFactory,
    ILogger<MemorySeedSyncStatusPersistence> logger)
{
    private static readonly TimeSpan FailureSaveTimeout = TimeSpan.FromSeconds(2);

    public async Task SaveAsync(MemorySeedSyncSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IMemorySeedSyncStatusWriter>();
        await writer.SaveAsync(snapshot, cancellationToken);
    }

    public async Task TrySaveFailureAsync(MemorySeedSyncSnapshot snapshot, CancellationToken cancellationToken)
    {
        using var boundedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        boundedCancellation.CancelAfter(FailureSaveTimeout);
        var saveTask = SaveAsync(snapshot, boundedCancellation.Token);
        try
        {
            await saveTask.WaitAsync(FailureSaveTimeout, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ObserveIncompleteSave(saveTask);
            LogFailureStatusSaveSkipped(logger, "cancelled");
        }
        catch (OperationCanceledException) when (boundedCancellation.IsCancellationRequested)
        {
            ObserveIncompleteSave(saveTask);
            LogFailureStatusSaveSkipped(logger, "timed_out");
        }
        catch (TimeoutException)
        {
            ObserveIncompleteSave(saveTask);
            LogFailureStatusSaveSkipped(logger, "timed_out");
        }
        catch (Exception exception)
        {
            LogFailureStatusSaveSkipped(logger, exception.GetType().Name);
        }
    }

    private static void ObserveIncompleteSave(Task saveTask)
    {
        if (!saveTask.IsCompleted)
        {
            _ = ObserveSaveAsync(saveTask);
        }
    }

    private static async Task ObserveSaveAsync(Task saveTask)
    {
        try
        {
            await saveTask.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    [LoggerMessage(LogLevel.Warning, "Memory seed failure status persistence was skipped: {Reason}.")]
    private static partial void LogFailureStatusSaveSkipped(ILogger logger, string reason);
}