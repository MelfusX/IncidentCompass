using System.Security.Cryptography;
using System.Text;

namespace IncidentCompass.Tester;

internal sealed record DemoScenario(
    string Id,
    string Name,
    string ServiceName,
    string ErrorType,
    string ErrorMessage,
    string Route,
    int SignalCount,
    string? ExpectedClassification,
    bool? ExpectedIsMassIssue,
    string? ExpectedEvidenceKind,
    IncidentEnvelope? ExactEnvelope = null,
    bool RequiresNoActionGate = false)
{
    public static IReadOnlyList<DemoScenario> CreateAll(string runId) =>
    [
        new(
            "1",
            "known-timeout-runbook",
            "checkout-api",
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

    public static DemoScenario CreateInjection(IncidentEnvelope envelope) =>
        new(
            "5",
            "injection-disabled-action-gate",
            envelope.ServiceName,
            envelope.Attributes.ErrorType,
            envelope.Attributes.ErrorMessage,
            envelope.Attributes.HttpRoute,
            1,
            null,
            null,
            null,
            envelope,
            true);

    public IncidentEnvelope CreateEnvelope(string runId, int index)
    {
        if (ExactEnvelope is not null)
        {
            return ExactEnvelope;
        }

        var externalId = $"demo-{runId}-{Id}-{index}";
        var httpRoute = Id == "1" ? Route + "/" + CreateRunToken(runId) : Route;
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
                httpRoute,
                "POST " + Route,
                Id == "4" ? 200 : 500),
            new Dictionary<string, object?>
            {
                ["demoScenario"] = Id,
                ["demoRunId"] = runId,
                ["eventIndex"] = index
            });
    }

    private static string CreateRunToken(string runId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(runId));
        return string.Create(
            16,
            hash,
            static (characters, bytes) =>
            {
                for (var index = 0; index < characters.Length; index++)
                {
                    characters[index] = (char)('g' + bytes[index] % 20);
                }
            });
    }
}
