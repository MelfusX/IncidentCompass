namespace IncidentCompass.Application.Intake.GetFault;

public sealed record TriageJobSummary(Guid Id, string Status, int Attempt, string ConfigHash, DateTimeOffset CreatedAtUtc);
