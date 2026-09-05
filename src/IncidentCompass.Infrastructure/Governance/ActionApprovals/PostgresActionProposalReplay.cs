using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionProposalReplay
{
    public static async Task<ActionApprovalRecord?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreparedActionProposal proposal,
        CancellationToken cancellationToken) =>
        await FindAsync(
            connection, transaction, proposal.TenantId, proposal.OriginReportId,
            proposal.ToolId, proposal.ProposalKey, cancellationToken);

    public static async Task<ActionApprovalRecord?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GovernedActionProposal proposal,
        CancellationToken cancellationToken) =>
        await FindAsync(
            connection, transaction, proposal.TenantId, proposal.OriginReportId,
            proposal.ToolId, proposal.ProposalKey, cancellationToken);

    private static async Task<ActionApprovalRecord?> FindAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string tenantId,
        Guid originReportId,
        string toolId,
        string proposalKey,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT " + PostgresActionApprovalReader.Columns + """
            FROM incidentcompass.action_approvals a
            WHERE a.tenant_id = @tenant_id AND a.origin_report_id = @report_id
              AND a.tool_id = @tool_id AND a.proposal_key = @proposal_key;
            """, connection, transaction);
        command.AddParameter("tenant_id", tenantId);
        command.AddParameter("report_id", originReportId);
        command.AddParameter("tool_id", toolId);
        command.AddParameter("proposal_key", proposalKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgresActionApprovalReader.Read(reader) : null;
    }

    public static async Task ValidateEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord existing,
        PreparedActionProposal proposal,
        CancellationToken cancellationToken) =>
        await ValidateEvidenceAsync(
            connection, transaction, existing, proposal.EvidenceArtifactIds, cancellationToken);

    public static async Task ValidateEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord existing,
        GovernedActionProposal proposal,
        CancellationToken cancellationToken) =>
        await ValidateEvidenceAsync(
            connection, transaction, existing, proposal.EvidenceArtifactIds, cancellationToken);

    private static async Task ValidateEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord existing,
        IReadOnlyList<Guid> evidenceArtifactIds,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM incidentcompass.action_approval_provenance
            WHERE action_id = @action_id AND source_type = 'artifact'
              AND source_id = ANY(@artifact_ids);
            """, connection, transaction);
        command.AddParameter("action_id", existing.Id);
        command.AddParameter("artifact_ids", evidenceArtifactIds.ToArray());
        var matched = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (matched != evidenceArtifactIds.Count || existing.ProvenanceCount != matched + 1)
        {
            throw new ConflictException("Action proposal idempotency key was reused with different provenance.");
        }
    }

    public static ActionProposalResult Validate(ActionApprovalRecord existing, PreparedActionProposal proposal)
    {
        var autoApproved = existing.DecisionActor == "system:policy";
        if (existing.Category != proposal.Category || existing.Mode != proposal.Mode ||
            existing.LogicalTargetId != proposal.LogicalTargetId ||
            existing.AdapterBindingFingerprint != proposal.AdapterBindingFingerprint ||
            !existing.CanonicalPayload.AsSpan().SequenceEqual(proposal.CanonicalPayload) ||
            existing.ReviewSummary != proposal.ReviewSummary || autoApproved != proposal.AutomaticallyApproved)
        {
            throw new ConflictException("Action proposal idempotency key was reused with different immutable input.");
        }

        return new ActionProposalResult(existing, true);
    }

    public static ActionProposalResult Validate(ActionApprovalRecord existing, GovernedActionProposal proposal)
    {
        if (existing.Category != proposal.RegisteredTool.Category ||
            existing.LogicalTargetId != proposal.RegisteredTool.LogicalTargetId ||
            existing.AdapterBindingFingerprint != proposal.AdapterBindingFingerprint ||
            !existing.CanonicalPayload.AsSpan().SequenceEqual(proposal.CanonicalPayload) ||
            existing.ReviewSummary != proposal.ReviewSummary)
        {
            throw new ConflictException("Action proposal idempotency key was reused with different immutable input.");
        }

        return new ActionProposalResult(existing, true);
    }
}
