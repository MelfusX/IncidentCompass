using System.Text.Json;
using IncidentCompass.Application.Tickets;

namespace IncidentCompass.UnitTests;

public sealed class TicketCreateEligibilityTests
{
    [Fact]
    public void ExactRepositoryBoundNoMatch_IsEligible()
    {
        using var document = JsonDocument.Parse(Output(
            outcome: "no_match", provider: "github", repository: "owner/repo", matched: false));

        Assert.True(TicketCreateEligibility.IsRepositoryBoundNoMatch(
            document.RootElement, "github", "owner/repo"));
    }

    [Theory]
    [InlineData("matched", "github", "owner/repo", true)]
    [InlineData("connector_unavailable", null, null, false)]
    [InlineData("no_match", "github", "other/repo", false)]
    [InlineData("no_match", "jira", "owner/repo", false)]
    public void NonAuthoritativeOutcome_IsIneligible(
        string outcome,
        string? provider,
        string? repository,
        bool matched)
    {
        using var document = JsonDocument.Parse(Output(outcome, provider, repository, matched));

        Assert.False(TicketCreateEligibility.IsRepositoryBoundNoMatch(
            document.RootElement, "github", "owner/repo"));
    }

    [Fact]
    public void NoMatchWithItems_IsIneligible()
    {
        using var document = JsonDocument.Parse("""
            {"matched":false,"message":"no matches","outcome":"no_match","provider":"github","repository":"owner/repo","code":"ticket_search_no_matches","items":[{"externalId":"42"}],"noMatchReason":"ticket_search_no_matches"}
            """);

        Assert.False(TicketCreateEligibility.IsRepositoryBoundNoMatch(
            document.RootElement, "github", "owner/repo"));
    }

    private static string Output(
        string outcome,
        string? provider,
        string? repository,
        bool matched) => JsonSerializer.Serialize(new
        {
            matched,
            message = outcome == "no_match" ? "no matches" : "other outcome",
            outcome,
            provider,
            repository,
            code = "ticket_search_no_matches",
            items = Array.Empty<object>(),
            noMatchReason = "ticket_search_no_matches"
        });
}
