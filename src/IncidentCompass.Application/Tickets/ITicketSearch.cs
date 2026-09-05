namespace IncidentCompass.Application.Tickets;

public interface ITicketSearch
{
    Task<TicketSearchResult> SearchAsync(
        TicketSearchRequest request,
        CancellationToken cancellationToken);
}
