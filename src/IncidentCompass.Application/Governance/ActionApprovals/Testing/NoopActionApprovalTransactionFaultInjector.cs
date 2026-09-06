namespace IncidentCompass.Application.Governance.ActionApprovals.Testing;

/// <summary>
/// Production binding for <see cref="IActionApprovalTransactionFaultInjector"/>. It never faults; the
/// consuming approval and dispatch transactions need something to call at each seam.
/// </summary>
internal sealed class NoopActionApprovalTransactionFaultInjector : IActionApprovalTransactionFaultInjector
{
    public Task AfterActionRowAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AfterProposalArtifactAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AfterProvenanceRowAsync(int ordinal, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeProposalLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeDecisionLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeDispatchLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeTerminalLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
