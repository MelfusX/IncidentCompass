namespace IncidentCompass.Application.Tickets;

public sealed record TicketSearchResult(
    TicketSearchOutcome Outcome,
    string Code,
    IReadOnlyList<TicketSearchMatch> Matches,
    string? Provider = null,
    string? Repository = null)
{
    public static TicketSearchResult NoMatch(
        string provider,
        string repository,
        string code = "ticket_search_no_matches") =>
        new(TicketSearchOutcome.NoMatch, code, [], provider, repository);

    public static TicketSearchResult Unavailable(string code) =>
        new(TicketSearchOutcome.ConnectorUnavailable, code, []);
}
