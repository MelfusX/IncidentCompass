using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Intake.FaultGrouping;

// Job is null only in the "attached to existing open fault" and "attached to closed fault,
// suppressed" branches, where no job lookup is performed by the coordinator in Phase 1 -- a
// later phase may choose to look up the existing job for those branches too, but it is NOT
// required for Phase 1's Done-when criteria.
public sealed record FaultGroupingOutcome(Fault Fault, TriageJob? Job, bool IsNewFault, bool IsNewJob, bool IsSuppressed);
