using IncidentCompass.Application.Governance.ActionApprovals.Testing;

namespace IncidentCompass.IntegrationTests;

internal sealed class ThrowingActionApprovalFaultInjector(ActionApprovalFaultPoint point)
    : IActionApprovalTransactionFaultInjector
{
    public Task AfterActionRowAsync(CancellationToken cancellationToken) =>
        ThrowIf(ActionApprovalFaultPoint.AfterActionRow);

    public Task AfterProposalArtifactAsync(CancellationToken cancellationToken) =>
        ThrowIf(ActionApprovalFaultPoint.AfterProposalArtifact);

    public Task AfterProvenanceRowAsync(int ordinal, CancellationToken cancellationToken) =>
        ThrowIf(ActionApprovalFaultPoint.AfterProvenanceRow);

    public Task BeforeProposalLedgerAsync(CancellationToken cancellationToken) =>
        ThrowIf(ActionApprovalFaultPoint.BeforeProposalLedger);

    public Task BeforeDecisionLedgerAsync(CancellationToken cancellationToken) =>
        ThrowIf(ActionApprovalFaultPoint.BeforeDecisionLedger);

    public Task BeforeDispatchLedgerAsync(CancellationToken cancellationToken) =>
        ThrowIf(ActionApprovalFaultPoint.BeforeDispatchLedger);

    public Task BeforeTerminalLedgerAsync(CancellationToken cancellationToken) =>
        ThrowIf(ActionApprovalFaultPoint.BeforeTerminalLedger);

    private Task ThrowIf(ActionApprovalFaultPoint current)
    {
        if (point == current)
        {
            throw new InvalidOperationException("Injected action approval transaction failure.");
        }

        return Task.CompletedTask;
    }
}
