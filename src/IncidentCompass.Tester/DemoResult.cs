namespace IncidentCompass.Tester;

internal sealed record DemoResult(
    DemoScenario Scenario,
    Guid? FaultId,
    Guid? ReportId,
    string? Classification,
    bool? IsMassIssue,
    string? LedgerUrl,
    string? ReportUrl,
    bool Passed,
    string Detail);
