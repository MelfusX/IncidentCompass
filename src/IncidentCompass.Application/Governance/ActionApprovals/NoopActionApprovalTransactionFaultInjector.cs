namespace IncidentCompass.Application.Governance.ActionApprovals;

public sealed class NoopActionApprovalTransactionFaultInjector : IActionApprovalTransactionFaultInjector
{
    public Task AfterActionRowAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AfterProposalArtifactAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task AfterProvenanceRowAsync(int ordinal, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeProposalLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeDecisionLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeDispatchLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task BeforeTerminalLedgerAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
