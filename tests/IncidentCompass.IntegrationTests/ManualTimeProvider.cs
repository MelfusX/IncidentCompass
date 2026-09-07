namespace IncidentCompass.IntegrationTests;

internal sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;

    public void Advance(TimeSpan duration) => utcNow = utcNow.Add(duration);
}
