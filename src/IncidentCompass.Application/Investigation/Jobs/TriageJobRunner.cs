using IncidentCompass.Application.Core.Observability;
using IncidentCompass.Application.Core.Resilience;
using IncidentCompass.Application.Core.Text;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class TriageJobRunner(
    ITriageJobRuntimeRepository runtimeRepository,
    ITriageConfigurationRepository configurationRepository,
    IClaimedTriageJobProcessor processor,
    TimeProvider timeProvider,
    IProviderOutageTracker? providerOutageTracker = null,
    IRuntimeTelemetry? telemetry = null) : ITriageJobRunner
{
    private const int MaxStoredErrorMessageLength = 1000;

    public Task<TriageJob?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        return runtimeRepository.ClaimNextAsync(workerId, leaseDuration, cancellationToken);
    }

    public Task<bool> RenewLeaseAsync(
        TriageJob job,
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken) =>
        runtimeRepository.RenewLeaseAsync(job, workerId, leaseDuration, cancellationToken);
    public async Task ProcessClaimedAsync(
        TriageJob job,
        string workerId,
        TriageJobProcessingSettings settings,
        CancellationToken cancellationToken)
    {
        var configurationLoaded = false;
        using var attemptTelemetry = telemetry?.StartJobAttempt();

        try
        {
            var configuration = await configurationRepository.GetByHashAsync(job.ConfigHash, cancellationToken);
            configurationLoaded = true;
            await processor.ProcessAsync(job, configuration, workerId, cancellationToken);
            telemetry?.RecordJobAttempt(RuntimeTelemetryOutcome.Succeeded);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            telemetry?.RecordJobAttempt(RuntimeTelemetryOutcome.Cancelled);
            throw;
        }
        catch (Exception exception)
        {
            var providerOutage = ProviderOutageExceptionClassifier.IsProviderOutage(exception);
            if (providerOutage)
            {
                telemetry?.RecordJobAttempt(RuntimeTelemetryOutcome.ProviderUnavailable);
                providerOutageTracker?.RecordProviderFailure();
            }

            if (!providerOutage)
            {
                telemetry?.RecordJobAttempt(RuntimeTelemetryOutcome.Failed);
            }

            await runtimeRepository.RecordAttemptFailureAsync(
                job,
                workerId,
                CreateFailure(job, settings, exception, configurationLoaded, providerOutage),
                CancellationToken.None);
        }
    }

    private TriageJobAttemptFailure CreateFailure(
        TriageJob job,
        TriageJobProcessingSettings settings,
        Exception exception,
        bool configurationLoaded,
        bool providerOutage)
    {
        if (providerOutage)
        {
            return new TriageJobAttemptFailure(
                TriageJobStatus.RetryPending,
                "provider_unavailable",
                "Triage delayed: provider unavailable.",
                timeProvider.GetUtcNow().Add(providerOutageTracker?.RetryDelay ?? settings.RetryDelay),
                TriageJobRetryBudgetDisposition.DoNotConsumeAttempt);
        }
        var maxAttempts = Math.Max(1, settings.MaxAttempts);
        var errorCode = configurationLoaded ? "triage_job_attempt_failed" : "config_snapshot_unavailable";
        if (job.Attempt >= maxAttempts)
        {
            return new TriageJobAttemptFailure(
                TriageJobStatus.DeadLettered,
                errorCode,
                NormalizeMessage(exception),
                NextAttemptAtUtc: null);
        }

        return new TriageJobAttemptFailure(
            TriageJobStatus.RetryPending,
            errorCode,
            NormalizeMessage(exception),
            timeProvider.GetUtcNow().Add(settings.RetryDelay));
    }

    private static string NormalizeMessage(Exception exception)
    {
        var message = string.IsNullOrWhiteSpace(exception.Message)
            ? exception.GetType().Name
            : exception.Message;

        return TextTruncator.Truncate(message, MaxStoredErrorMessageLength);
    }
}
