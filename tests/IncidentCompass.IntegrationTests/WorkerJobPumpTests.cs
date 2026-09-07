using IncidentCompass.Application.Core.Resilience;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IncidentCompass.IntegrationTests;

public sealed class WorkerJobPumpTests
{
    [Fact]
    public async Task FillAvailableSlotsAsync_StartsNoMoreThanMaxConcurrentJobs()
    {
        var processorRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new BlockingTriageJobRunner(availableJobs: 5, processorRelease.Task);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITriageJobRunner>(runner);
        services.AddSingleton<WorkerJobLeaseRenewer>();
        using var provider = services.BuildServiceProvider();
        var pump = ActivatorUtilities.CreateInstance<WorkerJobPump>(provider);
        var options = new WorkerOptions
        {
            MaxConcurrentJobs = 2,
            LeaseSeconds = 60,
            MaxAttempts = 3,
            RetryDelaySeconds = 1
        };

        var started = await pump.FillAvailableSlotsAsync(
            "worker-pump-test",
            options,
            TestContext.Current.CancellationToken);
        var secondFill = await pump.FillAvailableSlotsAsync(
            "worker-pump-test",
            options,
            TestContext.Current.CancellationToken);

        Assert.Equal(2, started);
        Assert.Equal(0, secondFill);
        Assert.Equal(2, pump.ActiveJobCount);
        Assert.Equal(2, runner.ClaimedCount);
        Assert.Equal(2, runner.StartedProcessingCount);

        processorRelease.SetResult();
        await WaitUntilAsync(() => runner.CompletedProcessingCount == 2);
        await pump.WaitForNextWakeAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, pump.ActiveJobCount);
    }


    [Fact]
    public async Task FillAvailableSlotsAsync_DoesNotClaimWhileProviderIsBackpressured()
    {
        var runner = new BlockingTriageJobRunner(availableJobs: 1, Task.CompletedTask);
        var tracker = new ProviderOutageTracker(
            Options.Create(new ProviderResilienceOptions { FailureThreshold = 1, BackpressureSeconds = 60 }),
            TimeProvider.System);
        tracker.RecordProviderFailure();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITriageJobRunner>(runner);
        services.AddSingleton<IProviderOutageTracker>(tracker);
        services.AddSingleton<WorkerJobLeaseRenewer>();
        using var provider = services.BuildServiceProvider();
        var pump = ActivatorUtilities.CreateInstance<WorkerJobPump>(provider);

        var started = await pump.FillAvailableSlotsAsync(
            "worker-provider-backpressure",
            new WorkerOptions { MaxConcurrentJobs = 1, LeaseSeconds = 60, MaxAttempts = 3, RetryDelaySeconds = 1 },
            TestContext.Current.CancellationToken);

        Assert.Equal(0, started);
        Assert.Equal(0, runner.ClaimedCount);
    }
    [Fact]
    public async Task FillAvailableSlotsAsync_OwnershipLossCancelsProcessing()
    {
        var runner = new OwnershipLossTriageJobRunner();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITriageJobRunner>(runner);
        services.AddSingleton<WorkerJobLeaseRenewer>();
        using var provider = services.BuildServiceProvider();
        var pump = ActivatorUtilities.CreateInstance<WorkerJobPump>(provider);
        var options = new WorkerOptions
        {
            MaxConcurrentJobs = 1,
            LeaseSeconds = 1,
            MaxAttempts = 3,
            RetryDelaySeconds = 1
        };

        Assert.Equal(1, await pump.FillAvailableSlotsAsync("worker-ownership-loss", options, TestContext.Current.CancellationToken));
        await runner.ProcessingCancelled.Task.WaitAsync(TestContext.Current.CancellationToken);
        await ObserveUntilNoActiveJobsAsync(pump);

        Assert.True(runner.RenewalCallCount > 0);
        Assert.Equal(0, pump.ActiveJobCount);
    }
    [Fact]
    public async Task DrainAsync_CancelsAndWaitsForActiveProcessing()
    {
        var processorRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new BlockingTriageJobRunner(availableJobs: 1, processorRelease.Task);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITriageJobRunner>(runner);
        services.AddSingleton<WorkerJobLeaseRenewer>();
        using var provider = services.BuildServiceProvider();
        var pump = ActivatorUtilities.CreateInstance<WorkerJobPump>(provider);
        var options = new WorkerOptions
        {
            MaxConcurrentJobs = 1,
            LeaseSeconds = 60,
            MaxAttempts = 3,
            RetryDelaySeconds = 1
        };

        Assert.Equal(1, await pump.FillAvailableSlotsAsync("worker-drain", options, TestContext.Current.CancellationToken));
        await WaitUntilAsync(() => runner.StartedProcessingCount == 1);
        await pump.DrainAsync();

        Assert.True(runner.ProcessingCancelled.Task.IsCompletedSuccessfully);
        Assert.Equal(0, pump.ActiveJobCount);
    }
    [Fact]
    public async Task WaitForNextWakeAsync_ReturnsWhenAnActiveJobCompletesBeforeTheDelayElapses()
    {
        var processorRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new BlockingTriageJobRunner(availableJobs: 1, processorRelease.Task);
        using var provider = BuildProvider(runner);
        var pump = ActivatorUtilities.CreateInstance<WorkerJobPump>(provider);

        Assert.Equal(1, await pump.FillAvailableSlotsAsync("worker-wake-completion", SlowPollOptions(), TestContext.Current.CancellationToken));
        await WaitUntilAsync(() => runner.StartedProcessingCount == 1);
        var wake = pump.WaitForNextWakeAsync(TimeSpan.FromMinutes(5), TestContext.Current.CancellationToken);
        Assert.False(wake.IsCompleted);

        processorRelease.SetResult();
        await wake.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await pump.ObserveCompletedAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, pump.ActiveJobCount);
    }

    [Fact]
    public async Task WaitForNextWakeAsync_ReturnsAfterTheDelayWhileJobsKeepRunning()
    {
        var runner = new BlockingTriageJobRunner(availableJobs: 1, new TaskCompletionSource().Task);
        using var provider = BuildProvider(runner);
        var pump = ActivatorUtilities.CreateInstance<WorkerJobPump>(provider);

        Assert.Equal(1, await pump.FillAvailableSlotsAsync("worker-wake-delay", SlowPollOptions(), TestContext.Current.CancellationToken));
        await WaitUntilAsync(() => runner.StartedProcessingCount == 1);
        var started = TimeProvider.System.GetTimestamp();
        await pump.WaitForNextWakeAsync(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);

        Assert.True(TimeProvider.System.GetElapsedTime(started) >= TimeSpan.FromMilliseconds(120));
        Assert.Equal(1, pump.ActiveJobCount);
        await pump.DrainAsync();
    }

    [Fact]
    public async Task WaitForNextWakeAsync_ObservesTheAbandonedDelayWhenCancellationRacesActiveJobs()
    {
        var runner = new BlockingTriageJobRunner(availableJobs: 1, new TaskCompletionSource().Task);
        using var provider = BuildProvider(runner);
        var pump = ActivatorUtilities.CreateInstance<WorkerJobPump>(provider);
        using var cancellation = new CancellationTokenSource();

        Assert.Equal(1, await pump.FillAvailableSlotsAsync("worker-wake-cancel", SlowPollOptions(), cancellation.Token));
        await WaitUntilAsync(() => runner.StartedProcessingCount == 1);
        await cancellation.CancelAsync();

        // The wait must still return rather than throw, and the delay it abandons must be observed
        // so no faulted task is left behind for the finalizer to report.
        await pump.WaitForNextWakeAsync(TimeSpan.FromMinutes(5), cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await pump.DrainAsync();
    }

    private static ServiceProvider BuildProvider(ITriageJobRunner runner)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(runner);
        services.AddSingleton<WorkerJobLeaseRenewer>();
        return services.BuildServiceProvider();
    }

    private static WorkerOptions SlowPollOptions() => new()
    {
        MaxConcurrentJobs = 1,
        LeaseSeconds = 60,
        MaxAttempts = 3,
        RetryDelaySeconds = 1
    };

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task ObserveUntilNoActiveJobsAsync(WorkerJobPump pump)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (pump.ActiveJobCount > 0)
        {
            await pump.WaitForNextWakeAsync(TimeSpan.FromMilliseconds(10), timeout.Token);
            await pump.ObserveCompletedAsync(timeout.Token);
        }
    }


    private sealed class OwnershipLossTriageJobRunner : ITriageJobRunner
    {
        private readonly TriageJob job = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TriageJobStatus.Processing,
            1,
            "worker-ownership-loss",
            DateTimeOffset.UtcNow.AddSeconds(1),
            null,
            null,
            null,
            "ownership-loss-config",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        private int renewalCallCount;

        public TaskCompletionSource ProcessingCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RenewalCallCount => Volatile.Read(ref renewalCallCount);

        public Task<TriageJob?> ClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<TriageJob?>(job);

        public Task<bool> RenewLeaseAsync(TriageJob job, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref renewalCallCount);
            return Task.FromResult(false);
        }

        public async Task ProcessClaimedAsync(
            TriageJob job,
            string workerId,
            TriageJobProcessingSettings settings,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ProcessingCancelled.TrySetResult();
                throw;
            }
        }
    }
    private sealed class BlockingTriageJobRunner(int availableJobs, Task processorRelease) : ITriageJobRunner
    {
        private int remainingJobs = availableJobs;

        private int claimedCount;
        private int startedProcessingCount;
        private int completedProcessingCount;

        public int ClaimedCount => Volatile.Read(ref claimedCount);

        public int StartedProcessingCount => Volatile.Read(ref startedProcessingCount);

        public int CompletedProcessingCount => Volatile.Read(ref completedProcessingCount);

        public TaskCompletionSource ProcessingCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TriageJob?> ClaimNextAsync(
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            if (remainingJobs <= 0)
            {
                return Task.FromResult<TriageJob?>(null);
            }

            remainingJobs--;
            Interlocked.Increment(ref claimedCount);
            return Task.FromResult<TriageJob?>(new TriageJob(
                Id: Guid.NewGuid(),
                FaultId: Guid.NewGuid(),
                Status: TriageJobStatus.Processing,
                Attempt: 1,
                LockedBy: workerId,
                LockedUntilUtc: DateTimeOffset.UtcNow.Add(leaseDuration),
                NextAttemptAtUtc: null,
                LastErrorCode: null,
                LastErrorMessage: null,
                ConfigHash: "pump-test-config",
                CreatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedAtUtc: DateTimeOffset.UtcNow));
        }

        public Task<bool> RenewLeaseAsync(
            TriageJob job,
            string workerId,
            TimeSpan leaseDuration,
            CancellationToken cancellationToken) => Task.FromResult(true);
        public async Task ProcessClaimedAsync(
            TriageJob job,
            string workerId,
            TriageJobProcessingSettings settings,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref startedProcessingCount);
            try
            {
                await processorRelease.WaitAsync(cancellationToken);
                Interlocked.Increment(ref completedProcessingCount);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ProcessingCancelled.TrySetResult();
                throw;
            }
        }
    }
}
