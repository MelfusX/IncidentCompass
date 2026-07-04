using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

internal sealed class TriageJobRunner(
    ITriageJobRuntimeRepository runtimeRepository,
    ITriageConfigurationRepository configurationRepository,
    IClaimedTriageJobProcessor processor,
    TimeProvider timeProvider) : ITriageJobRunner
{
    private const int MaxStoredErrorMessageLength = 1000;

    public Task<TriageJob?> ClaimNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        return runtimeRepository.ClaimNextAsync(workerId, leaseDuration, cancellationToken);
    }

    public async Task ProcessClaimedAsync(
        TriageJob job,
        string workerId,
        TriageJobProcessingSettings settings,
        CancellationToken cancellationToken)
    {
        var configurationLoaded = false;

        try
        {
            var configuration = await configurationRepository.GetByHashAsync(job.ConfigHash, cancellationToken);
            configurationLoaded = true;
            await processor.ProcessAsync(job, configuration, workerId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            await runtimeRepository.RecordAttemptFailureAsync(
                job,
                workerId,
                CreateFailure(job, settings, exception, configurationLoaded),
                CancellationToken.None);
        }
    }

    private TriageJobAttemptFailure CreateFailure(
        TriageJob job,
        TriageJobProcessingSettings settings,
        Exception exception,
        bool configurationLoaded)
    {
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

        return message.Length <= MaxStoredErrorMessageLength
            ? message
            : message[..MaxStoredErrorMessageLength];
    }
}
