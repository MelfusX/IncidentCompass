using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public interface ITriageJobRepository
{
    Task<TriageJob> InsertPendingAsync(Guid faultId, string configHash, CancellationToken cancellationToken);

    Task<TriageJob?> FindByFaultIdAsync(Guid faultId, CancellationToken cancellationToken);
}
