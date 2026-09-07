namespace IncidentCompass.Application.Investigation.Jobs.Testing;

/// <summary>
/// Test-only fault seam for the tool-result commit transaction. This is production code that exists
/// for testability, not dead code: it is the only way to simulate a crash <em>between</em> the tool
/// artifact insert and the <c>ToolResult</c> ledger insert, which happen inside one database
/// transaction. A decorator around <c>ITriageToolResultCommitter</c> cannot reach that point, because
/// from outside the service the transaction has already committed or rolled back as a unit.
/// Production composition always binds <see cref="NoopTriageToolResultCommitFaultInjector"/>; only the
/// integration tests replace it.
/// </summary>
internal interface ITriageToolResultCommitFaultInjector
{
    /// <summary>
    /// Invoked after the tool artifact row is inserted and before the ledger row is inserted, inside
    /// the same transaction. Throwing here must roll both rows back together.
    /// </summary>
    Task AfterArtifactInsertedAsync(CancellationToken cancellationToken);
}
