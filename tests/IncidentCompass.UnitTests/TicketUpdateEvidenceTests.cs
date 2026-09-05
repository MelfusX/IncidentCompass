using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Tickets;

namespace IncidentCompass.UnitTests;

public sealed class TicketUpdateEvidenceTests
{
    [Fact]
    public void Shape_ReturnsOnlyExactConfiguredTicketIdentity()
    {
        var payload = Payload();
        var json = CanonicalJsonSerializer.Canonicalize(payload);

        Assert.True(ExistingTicketEvidenceShape.TryReadTicketId(
            "ticket:github:owner/repo:42", json, "owner/repo", out var ticketId));
        Assert.Equal("42", ticketId);
        Assert.False(ExistingTicketEvidenceShape.TryReadTicketId(
            "ticket:github:other/repo:42", json, "owner/repo", out _));
        Assert.False(ExistingTicketEvidenceShape.TryReadTicketId(
            "ticket:github:owner/repo:42", json, "other/repo", out _));

        payload["issueNumber"] = "42";
        Assert.False(ExistingTicketEvidenceShape.TryReadTicketId(
            "ticket:github:owner/repo:42",
            CanonicalJsonSerializer.Canonicalize(payload),
            "owner/repo",
            out _));
    }

    private static JsonObject Payload() => new()
    {
        ["evidenceKind"] = "ExistingTicket",
        ["provider"] = "github",
        ["repository"] = "owner/repo",
        ["issueNumber"] = 42,
        ["title"] = "Checkout timeout",
        ["status"] = "open",
        ["assignee"] = null,
        ["createdAtUtc"] = "2026-08-29T00:00:00.0000000+00:00",
        ["url"] = "https://github.com/owner/repo/issues/42",
        ["score"] = 0.9
    };
}
