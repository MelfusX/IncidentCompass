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
        var configuration = await configurationRepository.GetByHashAsync(job.ConfigHash, cancellationToken);

        try
        {
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
                CreateFailure(job, settings, exception),
                CancellationToken.None);
        }
    }

    private TriageJobAttemptFailure CreateFailure(
        TriageJob job,
        TriageJobProcessingSettings settings,
        Exception exception)
    {
        var maxAttempts = Math.Max(1, settings.MaxAttempts);
        if (job.Attempt >= maxAttempts)
        {
            return new TriageJobAttemptFailure(
                TriageJobStatus.DeadLettered,
                "triage_job_attempt_failed",
                NormalizeMessage(exception),
                NextAttemptAtUtc: null);
        }

        return new TriageJobAttemptFailure(
            TriageJobStatus.RetryPending,
            "triage_job_attempt_failed",
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
