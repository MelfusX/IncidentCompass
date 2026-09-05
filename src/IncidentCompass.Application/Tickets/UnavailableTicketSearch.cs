namespace IncidentCompass.Application.Tickets;

internal sealed class UnavailableTicketSearch : ITicketSearch
{
    public Task<TicketSearchResult> SearchAsync(
        TicketSearchRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TicketSearchResult.Unavailable("ticket_search_unavailable"));
    }
}
