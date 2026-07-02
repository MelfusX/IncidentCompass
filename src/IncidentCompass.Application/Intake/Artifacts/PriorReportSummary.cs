namespace IncidentCompass.Application.Intake.Artifacts;

public sealed record PriorReportSummary(Guid? ReportId, string Summary, IReadOnlyList<string> Limitations);
