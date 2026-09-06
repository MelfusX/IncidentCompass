namespace IncidentCompass.Application.Governance.PostReportActions.Testing;

/// <summary>
/// Test-only fault seam for the post-report action publication intent. This is production code that
/// exists for testability, not dead code: the hook sits <em>between</em> the publication intent insert
/// and the commit of the same transaction that publishes the triage report, so a decorator around
/// <c>ITriageReportRepository</c> or <c>ITriageReportPublicationIntentWriter</c> cannot reach it. The
/// seam proves that a report and its evaluation intent are never split: if the intent write is lost,
/// the report is not published either, which is what keeps the downstream external action path
/// at-most-once. Production composition always binds
/// <see cref="NoopTriageReportPublicationIntentFaultInjector"/>; only the integration tests replace it.
/// </summary>
internal interface ITriageReportPublicationIntentFaultInjector
{
    /// <summary>
    /// Invoked after the publication intent row is inserted and before the publishing transaction
    /// commits. Throwing here must roll the report and the intent back together.
    /// </summary>
    Task AfterIntentInsertedAsync(CancellationToken cancellationToken);
}
