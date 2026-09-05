namespace IncidentCompass.Application.Tickets;

public interface ITicketActionHistory
{
    Task<TicketActionHistorySnapshot> ReadPriorAsync(
        Guid actionId,
        CancellationToken cancellationToken);
}
