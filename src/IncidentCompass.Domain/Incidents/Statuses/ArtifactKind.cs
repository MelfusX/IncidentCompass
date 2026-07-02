namespace IncidentCompass.Domain.Incidents;

// The citable subset used for grounding is {TriggerSignal, NeighborSet, PriorReport,
// RetrievedItem, ToolResult}. Phase 2 writes WorkerOutput as attempt-level working state;
// later grounding rules keep it non-citable.
public enum ArtifactKind { TriggerSignal, NeighborSet, PriorReport, RetrievedItem, ToolResult, WorkerOutput }
