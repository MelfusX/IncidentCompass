using IncidentCompass.Application.Investigation.Reports;
using IncidentCompass.Application.Investigation.Reports.Context;

namespace IncidentCompass.UnitTests;

public sealed class ContextOutcomeReportPolicyTests
{
    [Fact]
    public void Apply_AddsCanonicalLimitationsIdempotently()
    {
        var report = Report();
        var outcomes = new[]
        {
            new ReadOnlyContextOutcome("source_lookup", ReadOnlyContextOutcomeStatus.NoMatch, "source_no_match"),
            new ReadOnlyContextOutcome("ticket_search", ReadOnlyContextOutcomeStatus.ConnectorUnavailable, "ticket_search_timeout")
        };

        var first = ContextOutcomeReportPolicy.Apply(report, outcomes);
        var second = ContextOutcomeReportPolicy.Apply(first, outcomes);

        Assert.Equal(3, second.Limitations.Count);
        Assert.Contains("Read-only context source_lookup returned no matches (source_no_match).", second.Limitations);
        Assert.Contains("Read-only context ticket_search was unavailable (ticket_search_timeout).", second.Limitations);
    }

    [Fact]
    public void Apply_IgnoresUnknownToolsAndUnsanitizedCodes()
    {
        var report = Report();
        var outcomes = new[]
        {
            new ReadOnlyContextOutcome("other", ReadOnlyContextOutcomeStatus.NoMatch, "safe_code"),
            new ReadOnlyContextOutcome("source_lookup", ReadOnlyContextOutcomeStatus.NoMatch, "secret: value")
        };

        var result = ContextOutcomeReportPolicy.Apply(report, outcomes);

        Assert.Equal(report.Limitations, result.Limitations);
    }

    private static TriageReport Report() => new(
        TriageReportStatus.InsufficientEvidence,
        "summary",
        "Unknown",
        "Low",
        [],
        ["original"],
        "inspect");
}
