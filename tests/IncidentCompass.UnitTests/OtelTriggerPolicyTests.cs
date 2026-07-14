using System.Text.Json.Nodes;
using IncidentCompass.Application.Intake.Configuration;
using IncidentCompass.Application.Intake.IngestSignal;
using IncidentCompass.Application.Intake.Normalization;

namespace IncidentCompass.UnitTests;

public sealed class OtelTriggerPolicyTests
{
    [Fact]
    public void ErrorOnlyPolicy_RejectsAnInformationalSpan()
    {
        var result = OtelTriggerPolicy.ShouldTrigger(
            CreateCommand(serviceName: "orders", severity: "info"),
            OtelTriggerSettings.Default);

        Assert.False(result);
    }

    [Fact]
    public void ErrorOnlyPolicy_AcceptsAnExceptionAttribute()
    {
        var result = OtelTriggerPolicy.ShouldTrigger(
            CreateCommand(serviceName: "orders", severity: "info", errorType: "TimeoutException"),
            OtelTriggerSettings.Default);

        Assert.True(result);
    }

    [Fact]
    public void Policy_RequiresConfiguredServiceAndSeverity()
    {
        var settings = new OtelTriggerSettings(
            ErrorsOnly: false,
            ServiceAllowList: ["payments"],
            SeverityAllowList: ["warn"]);

        Assert.True(OtelTriggerPolicy.ShouldTrigger(CreateCommand("payments", "warn"), settings));
        Assert.False(OtelTriggerPolicy.ShouldTrigger(CreateCommand("orders", "warn"), settings));
        Assert.False(OtelTriggerPolicy.ShouldTrigger(CreateCommand("payments", "error"), settings));
    }

    private static IngestSignalCommand CreateCommand(string serviceName, string severity, string? errorType = null) =>
        new(
            SourceKind: "otel",
            ServiceName: serviceName,
            Environment: "local",
            Severity: severity,
            Summary: null,
            Description: null,
            ObservedAtUtc: null,
            TraceId: null,
            SpanId: null,
            ParentSpanId: null,
            ExternalId: null,
            Attributes: new JsonObject { ["exception.type"] = errorType },
            Payload: null);
}