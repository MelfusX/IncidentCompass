namespace IncidentCompass.Application.Investigation.Jobs;

public interface ITriageJobInvestigationContextRepository
{
    Task<TriageJobInvestigationContext> GetAsync(
        Guid jobId,
        CancellationToken cancellationToken);
}
