namespace IncidentCompass.Tester;

internal static class DemoExpectationChecker
{
    public static bool Matches(
        DemoScenario scenario,
        TriageReportResponse report,
        out string detail)
    {
        if (scenario.ExpectedClassification is not null &&
            !string.Equals(report.Classification, scenario.ExpectedClassification, StringComparison.Ordinal))
        {
            detail = "classification=" + report.Classification;
            return false;
        }

        if (scenario.ExpectedIsMassIssue.HasValue &&
            scenario.ExpectedIsMassIssue != report.IsMassIssue)
        {
            detail = "is_mass_issue=" + FormatNullableBool(report.IsMassIssue);
            return false;
        }

        if (scenario.ExpectedEvidenceKind is not null &&
            !report.Evidence.Any(evidence => string.Equals(evidence.Kind, scenario.ExpectedEvidenceKind, StringComparison.Ordinal)))
        {
            detail = "missing evidence kind " + scenario.ExpectedEvidenceKind;
            return false;
        }

        detail = "ok";
        return true;
    }

    private static string FormatNullableBool(bool? value) => value.HasValue ? value.Value.ToString().ToLowerInvariant() : "null";
}
