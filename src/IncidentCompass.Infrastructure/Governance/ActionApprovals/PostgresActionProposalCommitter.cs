using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.ActionApprovals.Testing;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Domain.Incidents.Statuses;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionProposalCommitter
{
    public static async Task<ActionApprovalRecord> InsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreparedActionProposal proposal,
        GroundedActionProposalContext origin,
        string payloadSha256,
        IActionApprovalTransactionFaultInjector faultInjector,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var action = CreateAction(proposal, origin, payloadSha256, timeProvider.GetUtcNow());
        await PostgresActionProposalWriter.InsertActionAsync(connection, transaction, action, cancellationToken);
        await faultInjector.AfterActionRowAsync(cancellationToken);
        await PostgresActionProposalWriter.InsertArtifactAsync(connection, transaction, action, cancellationToken);
        await faultInjector.AfterProposalArtifactAsync(cancellationToken);
        await PostgresActionProposalWriter.InsertProvenanceAsync(
            connection, transaction, action.Id, origin.Provenance, faultInjector, cancellationToken);
        await faultInjector.BeforeProposalLedgerAsync(cancellationToken);
        await WriteLedgerAsync(connection, transaction, action, origin, proposal, cancellationToken);
        return action;
    }

    private static ActionApprovalRecord CreateAction(
        PreparedActionProposal proposal,
        GroundedActionProposalContext origin,
        string payloadSha256,
        DateTimeOffset now)
    {
        var evidence = origin.Provenance
            .Where(static item => item.SourceType == "artifact")
            .Select(static item => new ActionProvenanceIdentity(item.SourceId, item.TrustClass));
        var provenanceSha256 = ActionApprovalContractV1.ComputeProvenanceSha256(proposal.OriginReportId, evidence);
        var approvalSha256 = ActionApprovalContractV1.ComputeApprovalSha256(
            proposal.OriginReportId, proposal.ToolId, proposal.Category, proposal.Mode,
            proposal.LogicalTargetId, proposal.AdapterBindingFingerprint, payloadSha256,
            proposal.CanonicalPayload, provenanceSha256);
        return new ActionApprovalRecord(
            Guid.NewGuid(), proposal.TenantId, proposal.OriginReportId, origin.FaultId, origin.JobId,
            origin.Attempt, proposal.ToolId, proposal.ProposalKey, proposal.Category, proposal.Mode,
            proposal.LogicalTargetId, proposal.AdapterBindingFingerprint,
            ActionApprovalLimits.ApprovalContractVersion, provenanceSha256,
            proposal.AutomaticallyApproved ? ActionApprovalState.Approved : ActionApprovalState.Requested,
            proposal.CanonicalPayload.ToArray(), payloadSha256, approvalSha256, Guid.NewGuid(),
            proposal.ReviewSummary, origin.Provenance.Count, now,
            now.AddMinutes(proposal.ApprovalTtlMinutes),
            proposal.AutomaticallyApproved ? "system:policy" : null,
            proposal.AutomaticallyApproved ? now : null,
            null, null, null, null, null, null, null, null, null);
    }

    private static async Task WriteLedgerAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord action,
        GroundedActionProposalContext origin,
        PreparedActionProposal proposal,
        CancellationToken cancellationToken)
    {
        var reference = "action:" + action.Id;
        var policyDecision = proposal.AutomaticallyApproved
            ? TriageLedgerDecision.Allowed
            : TriageLedgerDecision.ApprovalRequired;
        await PostgresActionLedgerWriter.InsertAsync(
            connection, transaction, origin, TriageLedgerEventType.PolicyDecision, action.ToolId,
            null, proposal.PolicyDecisionReason, policyDecision, null, reference, action.CreatedAtUtc, cancellationToken);
        await PostgresActionLedgerWriter.InsertAsync(
            connection, transaction, origin, TriageLedgerEventType.ActionProposed, action.ToolId,
            null, action.ReviewSummary, null, null, reference, action.CreatedAtUtc, cancellationToken);
        if (proposal.AutomaticallyApproved)
        {
            await PostgresActionLedgerWriter.InsertAsync(
                connection, transaction, origin, TriageLedgerEventType.ApprovalDecision, action.ToolId,
                "system:policy", proposal.PolicyDecisionReason, TriageLedgerDecision.AutoApproved,
                null, reference, action.CreatedAtUtc, cancellationToken);
        }
    }
}
