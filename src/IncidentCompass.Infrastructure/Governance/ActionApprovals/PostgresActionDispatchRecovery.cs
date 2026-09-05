using System.Text;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Domain.Incidents.Statuses;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Governance.ActionApprovals;

internal sealed class PostgresActionDispatchRecovery(IActionApprovalTransactionFaultInjector faultInjector)
{
    public async Task<bool> FailOutcomeUnknownAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ActionApprovalRecord action,
        CancellationToken cancellationToken)
    {
        if (action.State != ActionApprovalState.Approved ||
            action.DispatchStartedAtUtc is null ||
            action.DispatchDeadlineAtUtc is null)
        {
            return false;
        }

        var resultPayload = Encoding.UTF8.GetBytes("{\"code\":\"dispatch_outcome_unknown\"}");
        await using var command = new NpgsqlCommand("""
            UPDATE incidentcompass.action_approvals
            SET state = 'failed', result_payload = @result, result_summary = @summary,
                failure_code = @code, completed_at_utc = clock_timestamp()
            WHERE id = @action_id AND state = 'approved'
              AND dispatch_started_at IS NOT NULL
              AND dispatch_deadline_at <= clock_timestamp()
            RETURNING completed_at_utc;
            """, connection, transaction);
        command.AddParameter("result", resultPayload);
        command.AddParameter("summary", "The external action outcome is unknown after dispatch recovery.");
        command.AddParameter("code", "dispatch_outcome_unknown");
        command.AddParameter("action_id", action.Id);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null)
        {
            return false;
        }

        var completed = value is DateTimeOffset offset
            ? offset
            : new DateTimeOffset(DateTime.SpecifyKind((DateTime)value, DateTimeKind.Utc));
        var artifactId = await PostgresActionResultWriter.InsertAsync(
            connection, transaction, action, resultPayload,
            "The external action outcome is unknown after dispatch recovery.",
            "dispatch_outcome_unknown", completed, cancellationToken);
        await faultInjector.BeforeTerminalLedgerAsync(cancellationToken);
        var origin = await PostgresActionOriginContext.ReadAsync(
            connection, transaction, action, cancellationToken);
        await PostgresActionLedgerWriter.InsertAsync(
            connection, transaction, origin, TriageLedgerEventType.ActionCompleted, action.ToolId,
            "system:recovery", "dispatch_outcome_unknown", null, TriageLedgerToolStatus.Failed,
            "artifact:" + artifactId, completed, cancellationToken);
        return true;
    }
}
