using System.Text.Json.Nodes;
using IncidentCompass.Application.Core.Serialization;
using IncidentCompass.Application.Tickets;

namespace IncidentCompass.UnitTests;

public sealed class ExistingTicketEvidenceShapeTests
{
    [Fact]
    public void IsCitable_AcceptsOnlyClosedConfiguredGitHubIssueShape()
    {
        var payload = Payload();
        var json = CanonicalJsonSerializer.Canonicalize(payload);

        Assert.True(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42", json, "owner/repo"));
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:other/repo:42", json, "owner/repo"));
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42", json, "other/repo"));
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42", json, "Owner/repo"));

        payload["url"] = "https://GitHub.com/owner/repo/issues/42";
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42", CanonicalJsonSerializer.Canonicalize(payload), "owner/repo"));
        payload["url"] = "https://github.com/owner/repo/Issues/42";
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42", CanonicalJsonSerializer.Canonicalize(payload), "owner/repo"));
        payload["url"] = "HTTPS://github.com/owner/repo/issues/42";
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42", CanonicalJsonSerializer.Canonicalize(payload), "owner/repo"));

        payload["url"] = "https://github.com/owner/repo/issues/42";
        payload["body"] = "must not be persisted";
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42", CanonicalJsonSerializer.Canonicalize(payload), "owner/repo"));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"ticket\"")]
    [InlineData("42")]
    [InlineData("true")]
    public void IsCitable_NonObjectPayloadIsControlledNonCitable(string payload)
    {
        Assert.False(ExistingTicketEvidenceShape.IsCitable(
            "ticket:github:owner/repo:42",
            payload,
            "owner/repo"));
    }

    private static JsonObject Payload() => new()
    {
        ["evidenceKind"] = "ExistingTicket",
        ["provider"] = "github",
        ["repository"] = "owner/repo",
        ["issueNumber"] = 42,
        ["title"] = "Checkout timeout",
        ["status"] = "open",
        ["assignee"] = "octocat",
        ["createdAtUtc"] = "2026-01-15T00:00:00.0000000+00:00",
        ["url"] = "https://github.com/owner/repo/issues/42",
        ["score"] = 0.85
    };
}
