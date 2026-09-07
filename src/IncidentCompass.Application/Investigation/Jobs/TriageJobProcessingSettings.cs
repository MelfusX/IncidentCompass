namespace IncidentCompass.Application.Investigation.Jobs;

public sealed record TriageJobProcessingSettings(
    int MaxAttempts,
    TimeSpan RetryDelay);
