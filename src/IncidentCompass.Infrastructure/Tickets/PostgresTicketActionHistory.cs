using IncidentCompass.Application.Tickets;
using IncidentCompass.Application.Governance.ActionApprovals;
using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Domain.Incidents.Actions;
using IncidentCompass.Infrastructure.Governance.ActionApprovals;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using System.Security.Cryptography;
using System.Text;

namespace IncidentCompass.Infrastructure.Tickets;

internal sealed class PostgresTicketActionHistory(
    PostgresDataSourceProvider dataSourceProvider) : ITicketActionHistory
{
    private const int MaximumHistory = 16;

    public Task<TicketActionHistorySnapshot> ReadPriorAsync(
        Guid actionId,
        CancellationToken cancellationToken) =>
        PostgresOperation.ExecuteAsync(
            "read prior ticket action history",
            () => ReadAsync(actionId, cancellationToken));

    internal static async Task<string?> ValidateCreateProposalAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        GovernedActionProposal proposal,
        GroundedActionProposalContext origin,
        string? configuredRepository,
        CancellationToken cancellationToken)
    {
        if (configuredRepository is null)
        {
            return "ticket_create_binding_unavailable";
        }

        var expectedBinding = ExternalActionBinding.ComputeFingerprint(
            "github-issues",
            proposal.RegisteredTool.LogicalTargetId!,
            GitHubIssuesTicketSearch.Authority.AbsoluteUri,
            configuredRepository);
        if (!ActionProposalValidator.IsLowerHexSha256(proposal.AdapterBindingFingerprint) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedBinding),
                Encoding.ASCII.GetBytes(proposal.AdapterBindingFingerprint)))
        {
            return "ticket_create_binding_changed";
        }

        await using var command = new NpgsqlCommand("""
            SELECT a.redacted_payload::text
            FROM incidentcompass.triage_artifacts a
            JOIN incidentcompass.triage_ledger l
              ON l.job_id = a.job_id AND l.attempt = a.attempt
             AND l.event_type = 'ToolResult' AND l.tool_name = 'ticket_search'
             AND l.tool_status = 'Succeeded'
             AND l.payload_ref = 'artifact:' || a.id::text
            WHERE a.job_id = @job_id AND a.attempt = @attempt
              AND a.kind = 'ToolResult' AND a.domain_ref = 'tool:ticket_search'
            ORDER BY a.created_at_utc, a.id
            LIMIT 2;
            """, connection, transaction);
        command.AddParameter("job_id", origin.JobId);
        command.AddParameter("attempt", origin.Attempt);
        var outputs = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            outputs.Add(reader.GetString(0));
        }

        if (outputs.Count != 1)
        {
            return "ticket_create_no_match_required";
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(outputs[0]);
            return TicketCreateEligibility.IsRepositoryBoundNoMatch(
                document.RootElement, "github", configuredRepository)
                ? null
                : "ticket_create_no_match_required";
        }
        catch (System.Text.Json.JsonException)
        {
            return "ticket_create_no_match_required";
        }
    }

    private async Task<TicketActionHistorySnapshot> ReadAsync(
        Guid actionId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSourceProvider.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH current_action AS (
                SELECT tenant_id, fault_id, tool_id, adapter_binding_fingerprint,
                       created_at_utc, id
                FROM incidentcompass.action_approvals
                WHERE id = @action_id AND category = 'ticket_create'
            )
            SELECT a.state, a.mode, a.failure_code, a.canonical_payload,
                   a.result_payload, a.result_summary
            FROM incidentcompass.action_approvals a
            CROSS JOIN current_action c
            WHERE a.tenant_id = c.tenant_id AND a.fault_id = c.fault_id
              AND a.tool_id = c.tool_id AND a.id <> c.id
              AND a.adapter_binding_fingerprint = c.adapter_binding_fingerprint
              AND (a.created_at_utc, a.id) < (c.created_at_utc, c.id)
            ORDER BY a.created_at_utc DESC, a.id DESC
            LIMIT @limit;
            """, connection);
        command.AddParameter("action_id", actionId);
        command.AddParameter("limit", MaximumHistory + 1);
        var confirmedPayload = default(byte[]);
        string? confirmedSummary = null;
        var unknownMarkers = new List<string>();
        var hasPending = false;
        var hasUnsafeHistory = false;
        var count = 0;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            count++;
            if (count > MaximumHistory)
            {
                continue;
            }

            var state = reader.GetString(0);
            var mode = reader.GetString(1);
            var failureCode = reader.IsDBNull(2) ? null : reader.GetString(2);
            var canonicalPayload = (byte[])reader[3];
            var resultPayload = reader.IsDBNull(4) ? null : (byte[])reader[4];
            if (confirmedPayload is null &&
                state == ActionApprovalState.Executed.ToStorageValue() &&
                mode == ActionExecutionMode.Live.ToStorageValue() && resultPayload is not null)
            {
                confirmedPayload = resultPayload;
                confirmedSummary = reader.IsDBNull(5) ? null : reader.GetString(5);
            }
            else if (state == ActionApprovalState.Failed.ToStorageValue() &&
                failureCode == "dispatch_outcome_unknown")
            {
                var marker = GitHubIssueMarker.ReadFromCanonicalPayload(canonicalPayload);
                if (marker is not null && !unknownMarkers.Contains(marker, StringComparer.Ordinal))
                {
                    unknownMarkers.Add(marker);
                }
                else if (marker is null)
                {
                    hasUnsafeHistory = true;
                }
            }
            else if (state == ActionApprovalState.Requested.ToStorageValue() ||
                state == ActionApprovalState.Approved.ToStorageValue())
            {
                hasPending = true;
            }
        }

        return new TicketActionHistorySnapshot(
            confirmedPayload,
            confirmedSummary,
            unknownMarkers,
            hasPending,
            count > MaximumHistory,
            hasUnsafeHistory);
    }
}
