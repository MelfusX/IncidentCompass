using System.Text;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Application.Notifications;

public static class TelegramNotificationPayloadFactory
{
    public static ExternalActionPreparation Create(TelegramNotificationWorkflowInput input)
    {
        if (input.OriginReportId == Guid.Empty || string.IsNullOrWhiteSpace(input.RouteId))
        {
            throw new ArgumentException("Telegram notification input is invalid.", nameof(input));
        }

        var reportId = input.OriginReportId.ToString("N");
        var payload = new JsonObject
        {
            ["originReportId"] = reportId,
            ["routeId"] = input.RouteId,
            ["schemaVersion"] = 1,
            ["text"] = $"Incident report {reportId} is ready for review."
        };
        return new ExternalActionPreparation(
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
            $"Send a Telegram notification for incident report {reportId}.");
    }
}
