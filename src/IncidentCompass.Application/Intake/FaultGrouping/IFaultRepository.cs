using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public interface IFaultRepository
{
    // Matches status IN (Queued, Analyzing).
    Task<Fault?> FindOpenFaultAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        CancellationToken cancellationToken);

    // Matches status IN (Completed, Failed, InsufficientEvidence), ordered by CreatedAtUtc
    // DESC (most recent first). Used for BOTH the silence-window check and the
    // recurrence-link lookup by the caller.
    Task<Fault?> FindMostRecentClosedFaultAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        CancellationToken cancellationToken);

    // Attempts the insert; returns the inserted Fault (with any DB-assigned/defaulted values
    // reflected) on success, or null if a concurrent partial-unique-index conflict meant
    // nothing was inserted (caller must then re-resolve via FindOpenFaultAsync) -- this is a
    // race that only strong/groupable faults can hit; weak faults always succeed.
    Task<Fault?> TryInsertAsync(Fault fault, CancellationToken cancellationToken);

    // Requires an active intake unit of work. Serializes signal attachment with fault
    // terminalization so callers can re-resolve when the candidate is no longer open.
    Task<Fault?> FindByIdForUpdateAsync(Guid id, CancellationToken cancellationToken);

    Task<Fault?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
}
