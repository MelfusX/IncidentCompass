using IncidentCompass.Application.Core.ModelGateway;
using IncidentCompass.Application.Core.Resilience;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using Microsoft.Extensions.Options;

namespace IncidentCompass.UnitTests;

public sealed class TriageJobRunnerProviderOutageTests
{
    [Fact]
    public async Task ProcessClaimedAsync_ProviderOutageDelaysWithoutBurningAttemptLimit()
    {
        var now = DateTimeOffset.UtcNow;
        var recorder = new RecordingRuntimeRepository();
        var tracker = new ProviderOutageTracker(
            Options.Create(new ProviderResilienceOptions { FailureThreshold = 1, BackpressureSeconds = 60 }),
            new FixedTimeProvider(now));
        var runner = new TriageJobRunner(
            recorder,
            new StaticConfigurationRepository(),
            new ProviderFailingProcessor(),
            new FixedTimeProvider(now),
            tracker);
        var job = new TriageJob(
            Guid.NewGuid(), Guid.NewGuid(), TriageJobStatus.Processing, Attempt: 3, "worker", now.AddMinutes(1),
            null, null, null, "config", now, now);

        await runner.ProcessClaimedAsync(
            job,
            "worker",
            new TriageJobProcessingSettings(MaxAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1)),
            CancellationToken.None);

        var failure = Assert.Single(recorder.Failures);
        Assert.Equal(TriageJobStatus.RetryPending, failure.Status);
        Assert.Equal("provider_unavailable", failure.ErrorCode);
        Assert.Equal(TriageJobRetryBudgetDisposition.DoNotConsumeAttempt, failure.RetryBudgetDisposition);
        Assert.Equal("Triage delayed: provider unavailable.", failure.ErrorMessage);
        Assert.Equal(now.AddSeconds(60), failure.NextAttemptAtUtc);
        Assert.True(tracker.IsBackpressured);
    }

    private sealed class StaticConfigurationRepository : ITriageConfigurationRepository
    {
        public Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult<TriageConfiguration>(null!);

        public Task<TriageConfiguration> GetByHashAsync(string configHash, CancellationToken cancellationToken) => Task.FromResult<TriageConfiguration>(null!);
    }

    private sealed class ProviderFailingProcessor : IClaimedTriageJobProcessor
    {
        public Task ProcessAsync(TriageJob job, TriageConfiguration configuration, string workerId, CancellationToken cancellationToken) =>
            Task.FromException(new AiModelException("test-provider", "Service unavailable."));
    }

    private sealed class RecordingRuntimeRepository : ITriageJobRuntimeRepository
    {
        public List<TriageJobAttemptFailure> Failures { get; } = [];

        public Task<TriageJob?> ClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) => Task.FromResult<TriageJob?>(null);

        public Task<bool> RenewLeaseAsync(TriageJob job, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task RecordAttemptFailureAsync(TriageJob job, string workerId, TriageJobAttemptFailure failure, CancellationToken cancellationToken)
        {
            Failures.Add(failure);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
