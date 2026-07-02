namespace IncidentCompass.Domain.Incidents;

public enum TriageJobStatus { Pending, Processing, Succeeded, Failed, RetryPending, DeadLettered }
