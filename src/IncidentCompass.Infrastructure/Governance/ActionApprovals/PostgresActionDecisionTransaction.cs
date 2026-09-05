using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Domain.Incidents.Statuses;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed class PostgresActionDecisionTransaction(
    IActionApprovalTransactionFaultInjector faultInjector,
    TimeProvider timeProvider)
{
    public async Task<ActionDecisionResult> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionDecisionRequest request,
        CancellationToken cancellationToken)
    {
        var found = await PostgresActionApprovalQueries.FindAsync(
            connection, transaction, request.ActionId, request.TenantId, false, cancellationToken);
        if (found is null)
        {
            return new ActionDecisionResult(ActionDecisionOutcome.NotFound, null, null);
        }

        await PostgresFaultTransactionLock.LockAsync(connection, transaction, found.FaultId, cancellationToken);
        var action = await PostgresActionApprovalQueries.FindAsync(
            connection, transaction, request.ActionId, request.TenantId, true, cancellationToken);
        if (action is null)
        {
            return new ActionDecisionResult(ActionDecisionOutcome.NotFound, null, null);
        }

        if (!await PostgresActionApprovalQueries.IsCurrentReportAsync(connection, transaction, action, cancellationToken))
        {
            return Conflict(action, "origin_report_superseded");
        }

        if (action.State != ActionApprovalState.Requested)
        {
            return Conflict(action, "stale_state");
        }

        var now = timeProvider.GetUtcNow();
        if (action.ExpiresAtUtc <= now)
        {
            var expired = await WriteDecisionAsync(
                connection, transaction, action, ActionApprovalState.Expired,
                "system:expiry", null, now, TriageLedgerDecision.Expired, cancellationToken);
            return Conflict(expired, "expired");
        }

        if (action.PayloadSha256 != request.PayloadSha256 || action.ApprovalSha256 != request.ApprovalSha256)
        {
            return Conflict(action, "observed_hash_mismatch");
        }

        var approved = request.Decision == ActionDecisionKind.Approve;
        var updated = await WriteDecisionAsync(
            connection,
            transaction,
            action,
            approved ? ActionApprovalState.Approved : ActionApprovalState.Rejected,
            request.Actor,
            approved ? null : request.RejectionReason,
            now,
            approved ? TriageLedgerDecision.Approved : TriageLedgerDecision.Rejected,
            cancellationToken);
        return new ActionDecisionResult(ActionDecisionOutcome.Updated, updated, null);
    }

    private async Task<ActionApprovalRecord> WriteDecisionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord action,
        ActionApprovalState state,
        string actor,
        string? rejectionReason,
        DateTimeOffset now,
        TriageLedgerDecision decision,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.action_approvals
            SET state = @state, decision_actor = @actor, decision_at_utc = @now,
                rejection_reason = @reason,
                completed_at_utc = CASE WHEN @state IN ('rejected', 'expired') THEN @now ELSE NULL END
            WHERE id = @action_id AND state = 'requested';
            """, connection, transaction);
        command.AddParameter("state", state.ToStorageValue());
        command.AddParameter("actor", actor);
        command.AddParameter("now", now);
        command.AddParameter("reason", rejectionReason);
        command.AddParameter("action_id", action.Id);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            return action;
        }

        await faultInjector.BeforeDecisionLedgerAsync(cancellationToken);
        var origin = await PostgresActionOriginContext.ReadAsync(connection, transaction, action, cancellationToken);
        await PostgresActionLedgerWriter.InsertAsync(
            connection, transaction, origin, TriageLedgerEventType.ApprovalDecision, action.ToolId,
            actor, decision.ToString().ToLowerInvariant(), decision, null,
            "action:" + action.Id, now, cancellationToken);
        return action with
        {
            State = state,
            DecisionActor = actor,
            DecisionAtUtc = now,
            RejectionReason = rejectionReason,
            CompletedAtUtc = state is ActionApprovalState.Rejected or ActionApprovalState.Expired ? now : null
        };
    }

    private static ActionDecisionResult Conflict(ActionApprovalRecord action, string code) =>
        new(ActionDecisionOutcome.Conflict, action, code);
}
