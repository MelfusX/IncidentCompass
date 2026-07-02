namespace IncidentCompass.Application.Intake.Artifacts;

public interface IPriorReportSummaryProvider
{
    // Given a fault id (the recurrence_of target), returns the most recent prior report's
    // summary/limitations if one exists, else null. Phase 1 has no triage_reports table (that
    // arrives in Phase 5), so Wave 2 will register a trivial adapter that always returns null --
    // this interface exists now so the grounded-facts assembly logic is complete and testable,
    // and Phase 5 only has to swap the adapter.
    Task<PriorReportSummary?> FindLatestAsync(Guid faultId, CancellationToken cancellationToken);
}
