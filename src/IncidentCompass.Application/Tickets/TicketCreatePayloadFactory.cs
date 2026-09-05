using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;

namespace IncidentCompass.Application.Tickets;

public static class TicketCreatePayloadFactory
{
    private const string ProposalKeyPrefix = "post-report:v1:";

    public static ExternalActionPreparation Create(Guid originReportId, string proposalKey)
    {
        var expectedKey = $"{ProposalKeyPrefix}{originReportId:N}:{TicketCreateTool.ToolId}";
        if (originReportId == Guid.Empty ||
            !string.Equals(proposalKey, expectedKey, StringComparison.Ordinal))
        {
            throw new ArgumentException("Ticket create input is invalid.", nameof(proposalKey));
        }

        var reportId = originReportId.ToString("N");
        var marker = ComputeMarker(proposalKey, reportId);
        var title = $"Incident report {reportId[..12]} requires tracking";
        var body = $"Governed incident report reference: {reportId}.\n\n" +
            "Review the report and its cited evidence before acting.\n\n" +
            $"<!-- incidentcompass-ticket:{marker} -->";
        var payload = new JsonObject
        {
            ["body"] = body,
            ["marker"] = marker,
            ["originReportId"] = reportId,
            ["schemaVersion"] = 1,
            ["title"] = title
        };
        return new ExternalActionPreparation(
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(payload)),
            $"Create one ticket for incident report {reportId}.");
    }

    private static string ComputeMarker(string proposalKey, string reportId)
    {
        var value = Encoding.UTF8.GetBytes(proposalKey + "\n" + reportId);
        return Convert.ToHexStringLower(SHA256.HashData(value));
    }
}
