namespace IncidentCompass.Domain.Incidents.Statuses;

public enum TriageLedgerEventType
{
    Delegated,
    ToolProposed,
    PolicyDecision,
    ToolResult,
    WorkerCompleted,
    BudgetEvent,
    ReportPublished,
    ModelCall
}
