namespace IncidentCompass.Domain.Incidents;

// The citable subset used for grounding is {TriggerSignal, NeighborSet, PriorReport, RecurrenceState,
// RetrievedItem, ToolResult}. Phase 2 writes WorkerOutput as attempt-level working state;
// later grounding rules keep it non-citable.
public enum ArtifactKind
{
    TriggerSignal,
    NeighborSet,
    PriorReport,
    RecurrenceState,
    RetrievedItem,
    ToolResult,
    WorkerOutput,
    ProposedAction,
    ActionResult
}
