namespace IncidentCompass.Application.Core.Resilience;

public interface IProviderOutageTracker
{
    bool IsBackpressured { get; }

    TimeSpan RetryDelay { get; }

    void RecordProviderFailure();

    void RecordProviderSuccess();
}
