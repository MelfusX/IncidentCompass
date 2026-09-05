namespace IncidentCompass.Application.Governance.ActionApprovals;

public interface IActionApprovalTransactionFaultInjector
{
    Task AfterActionRowAsync(CancellationToken cancellationToken);
    Task AfterProposalArtifactAsync(CancellationToken cancellationToken);
    Task AfterProvenanceRowAsync(int ordinal, CancellationToken cancellationToken);
    Task BeforeProposalLedgerAsync(CancellationToken cancellationToken);
    Task BeforeDecisionLedgerAsync(CancellationToken cancellationToken);
    Task BeforeDispatchLedgerAsync(CancellationToken cancellationToken);
    Task BeforeTerminalLedgerAsync(CancellationToken cancellationToken);
}
