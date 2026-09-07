namespace IncidentCompass.Application.Core.Resilience;

public sealed class ProviderResilienceOptions
{
    public const string SectionName = "IncidentCompass:ProviderResilience";

    public int FailureThreshold { get; set; } = 2;

    public int BackpressureSeconds { get; set; } = 60;
}
