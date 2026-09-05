namespace IncidentCompass.Application.Tickets;

public sealed record TicketSearchResult(
    TicketSearchOutcome Outcome,
    string Code,
    IReadOnlyList<TicketSearchMatch> Matches)
{
    public static TicketSearchResult NoMatch(string code = "ticket_search_no_matches") =>
        new(TicketSearchOutcome.NoMatch, code, []);

    public static TicketSearchResult Unavailable(string code) =>
        new(TicketSearchOutcome.ConnectorUnavailable, code, []);
}
