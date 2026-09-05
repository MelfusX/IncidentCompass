using System.Diagnostics.Metrics;

namespace IncidentCompass.Api.Security;

internal sealed class ApiAuthenticationMetrics : IDisposable
{
    internal const string MeterName = "IncidentCompass.Api.Authentication";
    internal const string RejectionCounterName = "incidentcompass.api.authentication.rejections";

    private readonly Meter meter = new(MeterName);
    private readonly Counter<long> rejectionCounter;

    public ApiAuthenticationMetrics()
    {
        rejectionCounter = meter.CreateCounter<long>(RejectionCounterName);
    }

    public void RecordRejection(ApiAuthenticationRejectionOutcome outcome) =>
        rejectionCounter.Add(
            1,
            new KeyValuePair<string, object?>("outcome", outcome.ToString().ToLowerInvariant()));

    public void Dispose() => meter.Dispose();
}
