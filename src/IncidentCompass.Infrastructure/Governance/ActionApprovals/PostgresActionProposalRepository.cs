using IncidentCompass.Application.Core.Exceptions;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Domain.Incidents.Statuses;
using IncidentCompass.Infrastructure.Postgres;
using IncidentCompass.Infrastructure.Tickets;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed class PostgresActionProposalRepository(
    PostgresDataSourceProvider dataSourceProvider,
    IActionApprovalTransactionFaultInjector faultInjector,
    TimeProvider timeProvider,
    IOptions<GitHubIssuesOptions> ticketOptions) : IActionProposalRepository
{
    private readonly PostgresActionProvenanceGrounder grounder = new(ticketOptions.Value.ConfiguredRepository);

    public Task<ActionProposalResult> CreateAsync(
        PreparedActionProposal proposal,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "create action approval proposal",
            () => CreateTransactionAsync(proposal, cancellationToken));

    private async Task<ActionProposalResult> CreateTransactionAsync(
        PreparedActionProposal proposal,
        CancellationToken cancellationToken)
    {
        var payloadSha256 = ActionProposalValidator.ValidateAndComputePayloadHash(proposal);
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var existing = await FindExistingAsync(connection, transaction, proposal, cancellationToken);
            if (existing is not null)
            {
                await ValidateReplayEvidenceAsync(connection, transaction, existing, proposal, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ValidateReplay(existing, proposal);
            }

            var origin = await grounder.GroundAsync(connection, transaction, proposal, cancellationToken);
            existing = await FindExistingAsync(connection, transaction, proposal, cancellationToken);
            if (existing is not null)
            {
                await ValidateReplayEvidenceAsync(connection, transaction, existing, proposal, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ValidateReplay(existing, proposal);
            }

            var action = CreateAction(proposal, origin, payloadSha256);
            await PostgresActionProposalWriter.InsertActionAsync(connection, transaction, action, cancellationToken);
            await faultInjector.AfterActionRowAsync(cancellationToken);
            await PostgresActionProposalWriter.InsertArtifactAsync(connection, transaction, action, cancellationToken);
            await faultInjector.AfterProposalArtifactAsync(cancellationToken);
            await PostgresActionProposalWriter.InsertProvenanceAsync(
                connection, transaction, action.Id, origin.Provenance, faultInjector, cancellationToken);
            await faultInjector.BeforeProposalLedgerAsync(cancellationToken);
            await WriteLedgerAsync(connection, transaction, action, origin, proposal, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ActionProposalResult(action, false);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private ActionApprovalRecord CreateAction(
        PreparedActionProposal proposal,
        GroundedActionProposalContext origin,
        string payloadSha256)
    {
        var now = timeProvider.GetUtcNow();
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

    private static ActionProposalResult ValidateReplay(
        ActionApprovalRecord existing,
        PreparedActionProposal proposal)
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

    private static async Task ValidateReplayEvidenceAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord existing,
        PreparedActionProposal proposal,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT count(*)
            FROM incidentcompass.action_approval_provenance
            WHERE action_id = @action_id AND source_type = 'artifact'
              AND source_id = ANY(@artifact_ids);
            """, connection, transaction);
        command.AddParameter("action_id", existing.Id);
        command.AddParameter("artifact_ids", proposal.EvidenceArtifactIds.ToArray());
        var matched = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (matched != proposal.EvidenceArtifactIds.Count || existing.ProvenanceCount != matched + 1)
        {
            throw new ConflictException("Action proposal idempotency key was reused with different provenance.");
        }
    }

    private static async Task<ActionApprovalRecord?> FindExistingAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PreparedActionProposal proposal,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT " + PostgresActionApprovalReader.Columns + """
            FROM incidentcompass.action_approvals a
            WHERE a.tenant_id = @tenant_id AND a.origin_report_id = @report_id
              AND a.tool_id = @tool_id AND a.proposal_key = @proposal_key;
            """, connection, transaction);
        command.AddParameter("tenant_id", proposal.TenantId);
        command.AddParameter("report_id", proposal.OriginReportId);
        command.AddParameter("tool_id", proposal.ToolId);
        command.AddParameter("proposal_key", proposal.ProposalKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgresActionApprovalReader.Read(reader) : null;
    }
}
