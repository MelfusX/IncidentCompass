using Microsoft.Extensions.Options;

namespace IncidentCompass.Application.Core.Resilience;

public sealed class ProviderOutageTracker(
    IOptions<ProviderResilienceOptions> options,
    TimeProvider timeProvider) : IProviderOutageTracker
{
    private readonly object sync = new();
    private int consecutiveFailures;
    private DateTimeOffset? backpressuredUntilUtc;

    public bool IsBackpressured
    {
        get
        {
            lock (sync)
            {
                if (backpressuredUntilUtc is not { } until)
                {
                    return false;
                }

                if (until > timeProvider.GetUtcNow())
                {
                    return true;
                }

                backpressuredUntilUtc = null;
                return false;
            }
        }
    }

    public TimeSpan RetryDelay => TimeSpan.FromSeconds(options.Value.BackpressureSeconds);

    public void RecordProviderFailure()
    {
        lock (sync)
        {
            consecutiveFailures++;
            if (consecutiveFailures >= options.Value.FailureThreshold)
            {
                backpressuredUntilUtc = timeProvider.GetUtcNow().Add(RetryDelay);
            }
        }
    }

    public void RecordProviderSuccess()
    {
        lock (sync)
        {
            consecutiveFailures = 0;
            backpressuredUntilUtc = null;
        }
    }
}