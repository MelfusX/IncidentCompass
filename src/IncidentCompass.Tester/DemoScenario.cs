namespace IncidentCompass.Tester;

internal sealed record DemoScenario(
    string Id,
    string Name,
    string ServiceName,
    string ErrorType,
    string ErrorMessage,
    string Route,
    int SignalCount,
    string ExpectedClassification,
    bool? ExpectedIsMassIssue,
    string? ExpectedEvidenceKind)
{
    public static IReadOnlyList<DemoScenario> CreateAll(string runId) =>
    [
        new(
            "1",
            "known-timeout-runbook",
            "payments-api-" + runId,
            "TimeoutException",
            "Checkout call timed out while waiting on inventory after 30000ms.",
            "/checkout",
            1,
            "KnownIncident",
            false,
            "Runbook"),
        new(
            "2",
            "unknown-null-reference",
            "admin-api-" + runId,
            "NullReferenceException",
            "Admin summary page threw a null reference while rendering a beta widget.",
            "/admin/summary",
            1,
            "Unknown",
            false,
            null),
        new(
            "3",
            "provider-unavailable-flood",
            "provider-api-" + runId,
            "ProviderUnavailableException",
            "Provider unavailable while calling dependency.",
            "/provider",
            6,
            "SimpleKnownError",
            true,
            null),
        new(
            "4",
            "validation-noise",
            "validation-svc-" + runId,
            "ValidationNoise",
            "Synthetic validation noise from a monitor probe.",
            "/noise",
            1,
            "Noise",
            false,
            null)
    ];

    public IncidentEnvelope CreateEnvelope(string runId, int index)
    {
        var externalId = $"demo-{runId}-{Id}-{index}";
        return new IncidentEnvelope(
            "tester",
            ServiceName,
            "prod",
            Id == "1" || Id == "3" ? "critical" : "warning",
            DateTimeOffset.UtcNow,
            new IncidentCorrelation("trace-" + externalId, "span-" + externalId, externalId),
            new IncidentAttributes(
                ErrorType,
                ErrorMessage + " event " + index,
                Route,
                "POST " + Route,
                Id == "4" ? 200 : 500),
            new Dictionary<string, object?>
            {
                ["demoScenario"] = Id,
                ["demoRunId"] = runId,
                ["eventIndex"] = index
            });
    }
}