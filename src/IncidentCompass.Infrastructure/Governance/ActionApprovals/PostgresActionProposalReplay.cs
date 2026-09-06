using System.Globalization;
using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal static class PostgresActionProposalReplay
{
    public static async Task<string?> ApplyNotificationGuardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GovernedActionProposal proposal,
        GroundedActionProposalContext origin,
        CancellationToken cancellationToken)
    {
        var unclaimed = await ReadUnclaimedAsync(
            connection, transaction, proposal, origin.FaultId, cancellationToken);
        if (unclaimed.Count > 32)
        {
            return "notification_history_exceeded";
        }

        foreach (var action in unclaimed)
        {
            await PostgresActionProposalWriter.SupersedeAsync(
                connection, transaction, action, cancellationToken);
        }

        await using var command = new NpgsqlCommand("""
            SELECT CASE
                WHEN EXISTS (
                    SELECT 1 FROM incidentcompass.action_approvals a
                    WHERE a.tenant_id = @tenant_id AND a.fault_id = @fault_id
                      AND a.tool_id = @tool_id AND a.state = 'approved'
                      AND a.dispatch_started_at IS NOT NULL)
                    THEN 'notification_in_flight'
                WHEN EXISTS (
                    SELECT 1 FROM incidentcompass.action_approvals a
                    WHERE a.tenant_id = @tenant_id AND a.fault_id = @fault_id
                      AND a.tool_id = @tool_id AND a.mode = 'live'
                      AND a.dispatch_started_at >= clock_timestamp() - interval '30 minutes'
                      AND (a.state = 'executed' OR
                           (a.state = 'failed' AND a.failure_code = 'dispatch_outcome_unknown')))
                    THEN 'notification_cooldown'
                ELSE NULL
            END;
            """, connection, transaction);
        command.AddParameter("tenant_id", proposal.TenantId);
        command.AddParameter("fault_id", origin.FaultId);
        command.AddParameter("tool_id", proposal.ToolId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<IReadOnlyList<ActionApprovalRecord>> ReadUnclaimedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GovernedActionProposal proposal,
        Guid faultId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT " + PostgresActionApprovalReader.Columns + """
            FROM incidentcompass.action_approvals a
            WHERE a.tenant_id = @tenant_id AND a.fault_id = @fault_id
              AND a.tool_id = @tool_id AND a.state IN ('requested', 'approved')
              AND a.dispatch_started_at IS NULL
            ORDER BY a.created_at_utc, a.id
            LIMIT 33;
            """, connection, transaction);
        command.AddParameter("tenant_id", proposal.TenantId);
        command.AddParameter("fault_id", faultId);
        command.AddParameter("tool_id", proposal.ToolId);
        var rows = new List<ActionApprovalRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(PostgresActionApprovalReader.Read(reader));
        }

        return rows;
    }

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
        var matched = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        if (matched != evidenceArtifactIds.Count || existing.ProvenanceCount != matched + 1)
        {
            throw new ConflictException(
                "Action proposal idempotency key was reused with different provenance.",
                ApplicationErrorCodes.ActionProposalProvenanceConflict,
                "This request reused an idempotency key with different request provenance.");
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
            throw new ConflictException(
                "Action proposal idempotency key was reused with different immutable input.",
                ApplicationErrorCodes.ActionProposalInputConflict,
                "This request reused an idempotency key with different request input.");
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
            throw new ConflictException(
                "Action proposal idempotency key was reused with different immutable input.",
                ApplicationErrorCodes.ActionProposalInputConflict,
                "This request reused an idempotency key with different request input.");
        }

        return new ActionProposalResult(existing, true);
    }
}
