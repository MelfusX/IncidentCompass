using IncidentCompass.Application.Intake.IngestSignal;
using IncidentCompass.Application.Intake.Normalization;

namespace IncidentCompass.UnitTests;

public sealed class SignalNormalizerTests
{
    [Fact]
    public void UserReportNormalizer_ConvertsObservedAtToUtc()
    {
        var normalizer = new UserReportSignalNormalizer();
        var observed = new DateTimeOffset(2026, 7, 1, 15, 0, 0, TimeSpan.FromHours(3));
        var command = new IngestSignalCommand(
            SourceKind: "user",
            ServiceName: "payments-api",
            Environment: "prod",
            Severity: null,
            Summary: "Checkout is failing",
            Description: null,
            ObservedAtUtc: observed,
            TraceId: null,
            SpanId: null,
            ParentSpanId: null,
            ExternalId: null,
            Attributes: null,
            Payload: null);

        var normalized = normalizer.Normalize(command, DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.Zero, normalized.ObservedAtUtc.Offset);
        Assert.Equal(observed.ToUniversalTime(), normalized.ObservedAtUtc);
    }

    [Fact]
    public void UserReportNormalizer_TruncatesClientSuppliedSummaryAndDescription()
    {
        var normalizer = new UserReportSignalNormalizer();
        var command = CreateCommand("user", new string('s', 600), new string('d', 16010));

        var normalized = normalizer.Normalize(command, DateTimeOffset.UtcNow);

        Assert.Equal(500, normalized.Summary.Length);
        Assert.EndsWith("...", normalized.Summary, StringComparison.Ordinal);
        Assert.Equal(16000, normalized.Description!.Length);
        Assert.EndsWith("...", normalized.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void StructuredNormalizer_TruncatesClientSuppliedSummaryAndDescription()
    {
        var normalizer = new TesterSignalNormalizer();
        var command = CreateCommand("tester", new string('s', 600), new string('d', 16010));

        var normalized = normalizer.Normalize(command, DateTimeOffset.UtcNow);

        Assert.Equal(500, normalized.Summary.Length);
        Assert.EndsWith("...", normalized.Summary, StringComparison.Ordinal);
        Assert.Equal(16000, normalized.Description!.Length);
        Assert.EndsWith("...", normalized.Description, StringComparison.Ordinal);
    }

    private static IngestSignalCommand CreateCommand(string sourceKind, string? summary, string? description) =>
        new(
            SourceKind: sourceKind,
            ServiceName: "payments-api",
            Environment: "prod",
            Severity: null,
            Summary: summary,
            Description: description,
            ObservedAtUtc: null,
            TraceId: null,
            SpanId: null,
            ParentSpanId: null,
            ExternalId: null,
            Attributes: null,
            Payload: null);
}
