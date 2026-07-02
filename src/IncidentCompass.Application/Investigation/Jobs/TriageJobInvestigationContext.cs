using IncidentCompass.Domain.Incidents;

namespace IncidentCompass.Application.Investigation.Jobs;

public sealed record TriageJobInvestigationContext(
    Fault Fault,
    Signal TriggerSignal,
    IReadOnlyCollection<TriageArtifact> JobArtifacts);
