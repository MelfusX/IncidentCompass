using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Application.Tickets;
using IncidentCompass.Domain.Incidents.Actions;

namespace IncidentCompass.UnitTests;

public sealed class TicketCreateToolTests
{
    [Fact]
    public void Descriptor_IsExternalAndProviderNeutral()
    {
        var descriptor = TicketCreateTool.Descriptor;

        Assert.Equal("ticket_create", descriptor.ToolId);
        Assert.Equal(AgentToolCapability.ExternalAction, descriptor.Capability);
        Assert.Equal(ActionCategory.TicketCreate, descriptor.Category);
        Assert.Equal("ticket:configured-repository", descriptor.LogicalTargetId);
        Assert.DoesNotContain("github", descriptor.LogicalTargetId!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payload_IsDeterministicCanonicalAndContainsNoProviderAuthority()
    {
        var reportId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var proposalKey = $"post-report:v1:{reportId:N}:ticket_create";

        var first = TicketCreatePayloadFactory.Create(reportId, proposalKey);
        var second = TicketCreatePayloadFactory.Create(reportId, proposalKey);
        using var document = JsonDocument.Parse(first.CanonicalPayload);
        var root = document.RootElement;

        Assert.Equal(first.CanonicalPayload, second.CanonicalPayload);
        Assert.Equal(64, root.GetProperty("marker").GetString()!.Length);
        Assert.Matches("^[0-9a-f]{64}$", root.GetProperty("marker").GetString()!);
        Assert.Contains(root.GetProperty("marker").GetString()!, root.GetProperty("body").GetString(), StringComparison.Ordinal);
        Assert.True(first.CanonicalPayload.AsSpan().SequenceEqual(
            Encoding.UTF8.GetBytes(CanonicalJsonSerializer.Canonicalize(
                JsonNode.Parse(first.CanonicalPayload)))));
        Assert.DoesNotContain("github", Encoding.UTF8.GetString(first.CanonicalPayload), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("repository", Encoding.UTF8.GetString(first.CanonicalPayload), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Payload_RejectsAProposalKeyForAnotherReportOrTool()
    {
        var reportId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            TicketCreatePayloadFactory.Create(reportId, "post-report:v1:other:ticket_create"));
        Assert.Throws<ArgumentException>(() =>
            TicketCreatePayloadFactory.Create(reportId, $"post-report:v1:{reportId:N}:ticket_update"));
    }
}
