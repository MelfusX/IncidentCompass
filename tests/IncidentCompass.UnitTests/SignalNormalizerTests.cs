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
            ExternalId: null,
            Attributes: null,
            Payload: null);

        var normalized = normalizer.Normalize(command, DateTimeOffset.UtcNow);

        Assert.Equal(TimeSpan.Zero, normalized.ObservedAtUtc.Offset);
        Assert.Equal(observed.ToUniversalTime(), normalized.ObservedAtUtc);
    }
}