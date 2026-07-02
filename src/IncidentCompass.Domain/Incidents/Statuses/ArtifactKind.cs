namespace IncidentCompass.Domain.Incidents;

// The citable subset used for grounding is {TriggerSignal, NeighborSet, PriorReport,
// RetrievedItem, ToolResult} -- WorkerOutput is not citable (future phases enforce this
// at grounding time; Phase 1 just needs the enum to exist with all six values).
public enum ArtifactKind { TriggerSignal, NeighborSet, PriorReport, RetrievedItem, ToolResult, WorkerOutput }
