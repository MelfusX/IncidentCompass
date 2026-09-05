namespace IncidentCompass.Application.Governance.ActionApprovals;

public interface IActionProposalRepository
{
    Task<ActionProposalResult> CreateAsync(
        PreparedActionProposal proposal,
        CancellationToken cancellationToken);
}
