using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

public interface ISignalRepository
{
    // Signals are inserted with whatever FaultId/IsSuppressed/SuppressedByFaultId values are
    // already on the record. For the two branches where a fault must be CREATED, callers
    // insert the signal FIRST with FaultId = null (to satisfy the faults.trigger_signal_id ->
    // signals.id FK before the fault row exists) and then call AttachToFaultAsync after the
    // fault is inserted -- this resolves the circular FK between signals.fault_id and
    // faults.trigger_signal_id.
    Task InsertAsync(Signal signal, CancellationToken cancellationToken);

    Task AttachToFaultAsync(Guid signalId, Guid faultId, CancellationToken cancellationToken);

    // Counts signals matching the grouping key with observed_at_utc in [windowStartUtc,
    // windowEndUtc] inclusive, counting each DISTINCT COALESCE(external_id, trace_id || ':' ||
    // span_id, id) value once (an uncorrelated signal with no external_id/trace/span counts as
    // itself via its own id).
    Task<int> CountDistinctNeighborsAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        DateTimeOffset windowStartUtc,
        DateTimeOffset windowEndUtc,
        CancellationToken cancellationToken);
}
