namespace IncidentCompass.Application.Tickets;

public interface ITicketUpdateEvidenceResolver
{
    Task<TicketUpdateEvidence?> ResolveAsync(
        string tenantId,
        Guid originReportId,
        CancellationToken cancellationToken);
}
