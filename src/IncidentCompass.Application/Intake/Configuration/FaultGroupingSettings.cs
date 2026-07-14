namespace IncidentCompass.Application.Intake.Configuration;

public sealed record FaultGroupingSettings(
    int LookbackMinutes,
    int SilenceWindowMinutes,
    int FingerprintVersion,
    MassIssueSettings MassIssue,
    IReadOnlyCollection<FingerprintRuleSettings>? FingerprintRules = null,
    IReadOnlyCollection<SuppressionRuleSettings>? SuppressionRules = null,
    RecurrenceSettings? Recurrence = null)
{
    public IReadOnlyCollection<FingerprintRuleSettings> Rules => FingerprintRules ?? [];
    public IReadOnlyCollection<SuppressionRuleSettings> SuppressionPolicies => SuppressionRules ?? [];
}