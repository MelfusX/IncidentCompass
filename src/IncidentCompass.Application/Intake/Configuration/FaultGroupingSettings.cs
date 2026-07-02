namespace IncidentCompass.Application.Intake.Configuration;

public sealed record FaultGroupingSettings(int LookbackMinutes, int SilenceWindowMinutes, int FingerprintVersion, MassIssueSettings MassIssue);
