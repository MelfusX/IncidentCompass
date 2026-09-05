using IncidentCompass.Application.Core.Resilience;
using Microsoft.Extensions.Options;

namespace IncidentCompass.UnitTests;

public sealed class ProviderOutageTrackerTests
{
    [Fact]
    public void FailureThreshold_BackpressuresUntilCooldownAndSuccessClearsState()
    {
        var now = DateTimeOffset.UtcNow;
        var timeProvider = new MutableTimeProvider(now);
        var tracker = new ProviderOutageTracker(
            Options.Create(new ProviderResilienceOptions { FailureThreshold = 2, BackpressureSeconds = 30 }),
            timeProvider);

        tracker.RecordProviderFailure();
        Assert.False(tracker.IsBackpressured);

        tracker.RecordProviderFailure();
        Assert.True(tracker.IsBackpressured);

        timeProvider.Advance(TimeSpan.FromSeconds(31));
        Assert.False(tracker.IsBackpressured);

        tracker.RecordProviderFailure();
        tracker.RecordProviderFailure();
        Assert.True(tracker.IsBackpressured);
        tracker.RecordProviderSuccess();
        Assert.False(tracker.IsBackpressured);
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset current = now;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current = current.Add(duration);
    }
}
