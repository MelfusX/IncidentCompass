namespace IncidentCompass.Application.Investigation.Jobs.Testing;

/// <summary>
/// Test-only fault seam for the triage-report publication transaction. This is production code that
/// exists for testability, not dead code: it is the only way to simulate a crash <em>between</em>
/// marking the job and fault terminal and writing the <c>ReportPublished</c> ledger event, which all
/// happen inside one database transaction. A decorator around <c>ITriageReportRepository</c> cannot
/// reach that point, because from outside the repository the transaction has already committed or
/// rolled back as a unit. Production composition always binds
/// <see cref="NoopTriageReportFinalCommitFaultInjector"/>; only the integration tests replace it.
/// </summary>
internal interface ITriageReportFinalCommitFaultInjector
{
    /// <summary>
    /// Invoked after the report, evidence and terminal status rows are written and before the
    /// <c>ReportPublished</c> ledger row is inserted, inside the same transaction. Throwing here must
    /// roll the whole publication back, leaving the job re-runnable.
    /// </summary>
    Task BeforeReportPublishedLedgerEventAsync(CancellationToken cancellationToken);
}
