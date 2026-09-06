using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Investigation.Jobs;
using IncidentCompass.Domain.Incidents;
using Microsoft.Extensions.Logging;

namespace IncidentCompass.UnitTests;

/// <summary>
/// The governed investigation path must leave an application-log trace of why an attempt failed
/// and of the retry-versus-dead-letter decision, without copying prompt or payload content into
/// the log. The failure trace must also survive a failing durable write.
/// </summary>
public sealed class TriageJobRunnerLoggingTests
{
    private const string PromptLikeSecret = "prompt-body-must-not-reach-logs";

    [Fact]
    public async Task ProcessClaimedAsync_DeadLetteredAttemptLogsBoundedFailureAndDispositionEvents()
    {
        var logger = new RecordingLogger<TriageJobRunner>();
        var runner = CreateRunner(new RecordingRuntimeRepository(), logger);

        await runner.ProcessClaimedAsync(
            CreateJob(attempt: 1),
            "worker-test",
            new TriageJobProcessingSettings(MaxAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        var failure = logger.Single(3101);
        Assert.Equal(LogLevel.Warning, failure.Level);
        Assert.Contains("triage_job_attempt_failed", failure.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), failure.Message, StringComparison.Ordinal);

        var deadLettered = logger.Single(3103);
        Assert.Equal(LogLevel.Error, deadLettered.Level);
        Assert.Contains("dead-lettered", deadLettered.Message, StringComparison.Ordinal);

        Assert.Equal(-1, logger.IndexOf(3102));
        AssertNoSensitiveContent(logger);
    }

    [Fact]
    public async Task ProcessClaimedAsync_RetryableAttemptLogsRetryDispositionInsteadOfDeadLetter()
    {
        var logger = new RecordingLogger<TriageJobRunner>();
        var runner = CreateRunner(new RecordingRuntimeRepository(), logger);

        await runner.ProcessClaimedAsync(
            CreateJob(attempt: 1),
            "worker-test",
            new TriageJobProcessingSettings(MaxAttempts: 3, RetryDelay: TimeSpan.FromSeconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(LogLevel.Warning, logger.Single(3101).Level);
        Assert.Equal(LogLevel.Information, logger.Single(3102).Level);
        Assert.Equal(-1, logger.IndexOf(3103));
        AssertNoSensitiveContent(logger);
    }

    [Fact]
    public async Task ProcessClaimedAsync_FailingFailurePersistenceKeepsTheOriginalFailureLoggedFirst()
    {
        var logger = new RecordingLogger<TriageJobRunner>();
        var runner = CreateRunner(new ThrowingRuntimeRepository(), logger);

        var persistenceFailure = await Assert.ThrowsAsync<InvalidTimeZoneException>(() =>
            runner.ProcessClaimedAsync(
                CreateJob(attempt: 1),
                "worker-test",
                new TriageJobProcessingSettings(MaxAttempts: 1, RetryDelay: TimeSpan.FromSeconds(1)),
                TestContext.Current.CancellationToken));

        Assert.Equal("durable failure write unavailable", persistenceFailure.Message);

        var originalFailureIndex = logger.IndexOf(3101);
        var dispositionIndex = logger.IndexOf(3103);
        var persistenceFailureIndex = logger.IndexOf(3105);

        Assert.True(originalFailureIndex >= 0, "The original attempt failure must be logged.");
        Assert.True(persistenceFailureIndex >= 0, "The secondary persistence failure must be logged.");
        Assert.True(
            originalFailureIndex < persistenceFailureIndex,
            "The original failure must be logged before the durable write is attempted.");
        Assert.True(dispositionIndex > originalFailureIndex && dispositionIndex < persistenceFailureIndex);

        var persistence = logger.Single(3105);
        Assert.Equal(LogLevel.Error, persistence.Level);
        Assert.Contains(nameof(InvalidTimeZoneException), persistence.Message, StringComparison.Ordinal);
        AssertNoSensitiveContent(logger);
    }

    private static void AssertNoSensitiveContent(RecordingLogger<TriageJobRunner> logger)
    {
        Assert.NotEmpty(logger.Entries);
        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(PromptLikeSecret, entry.Message, StringComparison.Ordinal);
            Assert.Null(entry.Exception);
        });
    }

    private static TriageJobRunner CreateRunner(
        ITriageJobRuntimeRepository runtimeRepository,
        RecordingLogger<TriageJobRunner> logger) =>
        new(
            runtimeRepository,
            new StaticConfigurationRepository(),
            new FailingProcessor(),
            TimeProvider.System,
            providerOutageTracker: null,
            telemetry: null,
            logger);

    private static TriageJob CreateJob(int attempt)
    {
        var now = DateTimeOffset.UtcNow;
        return new TriageJob(
            Guid.NewGuid(), Guid.NewGuid(), TriageJobStatus.Processing, attempt, "worker-test",
            now.AddMinutes(1), null, null, null, "config-hash", now, now);
    }

    private sealed class StaticConfigurationRepository : ITriageConfigurationRepository
    {
        public Task<TriageConfiguration> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(TestTriageConfiguration.Create());

        public Task<TriageConfiguration> GetByHashAsync(string configHash, CancellationToken cancellationToken) =>
            Task.FromResult(TestTriageConfiguration.Create());
    }

    private sealed class FailingProcessor : IClaimedTriageJobProcessor
    {
        public Task ProcessAsync(
            TriageJob job,
            TriageConfiguration configuration,
            string workerId,
            CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(PromptLikeSecret));
    }

    private sealed class RecordingRuntimeRepository : ITriageJobRuntimeRepository
    {
        public Task<TriageJob?> ClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<TriageJob?>(null);

        public Task<bool> RenewLeaseAsync(TriageJob job, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task RecordAttemptFailureAsync(TriageJob job, string workerId, TriageJobAttemptFailure failure, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingRuntimeRepository : ITriageJobRuntimeRepository
    {
        public Task<TriageJob?> ClaimNextAsync(string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult<TriageJob?>(null);

        public Task<bool> RenewLeaseAsync(TriageJob job, string workerId, TimeSpan leaseDuration, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task RecordAttemptFailureAsync(TriageJob job, string workerId, TriageJobAttemptFailure failure, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidTimeZoneException("durable failure write unavailable"));
    }
}
