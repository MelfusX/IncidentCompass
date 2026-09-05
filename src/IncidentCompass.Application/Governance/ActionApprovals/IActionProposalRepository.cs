namespace IncidentCompass.Application.Governance.ActionApprovals;

public interface IActionProposalRepository
{
    Task<ActionProposalOrigin?> FindSafeOriginAsync(
        string tenantId,
        Guid originReportId,
        CancellationToken cancellationToken);

    Task<bool> RecordDenialAsync(
        string tenantId,
        Guid originReportId,
        string? auditedToolId,
        string reasonCode,
        CancellationToken cancellationToken);

    Task<ActionProposalResult> CreateAsync(
        PreparedActionProposal proposal,
        CancellationToken cancellationToken);

    Task<ActionProposalResult> CreateGovernedAsync(
        GovernedActionProposal proposal,
        CancellationToken cancellationToken);
}
