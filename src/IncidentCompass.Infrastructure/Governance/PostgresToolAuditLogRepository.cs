using IncidentCompass.Application.Governance.Tools;
using IncidentCompass.Domain.Governance;
using System.Text.Json;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;
using NpgsqlTypes;

namespace IncidentCompass.Infrastructure.Governance;

/// DORMANT: reserved for IC-BL-010 governed tool execution work.
internal sealed class PostgresToolAuditLogRepository(PostgresDataSourceProvider dataSourceProvider)
    : IToolAuditLogRepository
{
    public async Task AddAsync(
        ToolAuditLogEntry entry,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.tool_audit_logs (
                id, conversation_id, tenant_id, user_id, correlation_id, tool_call_id,
                tool_name, schema_version, policy_version, validation_status, policy_decision,
                approval_state, execution_status, arguments, output, error_code,
                error_message, created_at_utc)
            VALUES (
                @id, @conversation_id, @tenant_id, @user_id, @correlation_id, @tool_call_id,
                @tool_name, @schema_version, @policy_version, @validation_status, @policy_decision,
                @approval_state, @execution_status, @arguments, @output, @error_code,
                @error_message, @created_at_utc);
            """, connection);

        command.AddParameter("id", entry.Id);
        command.AddParameter("conversation_id", entry.ConversationId);
        command.AddParameter("tenant_id", entry.TenantId);
        command.AddParameter("user_id", entry.UserId);
        command.AddParameter("correlation_id", entry.CorrelationId);
        command.AddParameter("tool_call_id", entry.ToolCallId);
        command.AddParameter("tool_name", entry.ToolName);
        command.AddParameter("schema_version", entry.SchemaVersion);
        command.AddParameter("policy_version", entry.PolicyVersion);
        command.AddParameter("validation_status", entry.ValidationStatus);
        command.AddParameter("policy_decision", entry.PolicyDecision);
        command.AddParameter("approval_state", entry.ApprovalState);
        command.AddParameter("execution_status", entry.ExecutionStatus);
        command.AddJsonbParameter("arguments", entry.Arguments.GetRawText());
        command.AddJsonbParameter("output", entry.Output?.GetRawText());
        command.AddParameter("error_code", entry.ErrorCode);
        command.AddParameter("error_message", entry.ErrorMessage);
        command.AddParameter("created_at_utc", entry.CreatedAtUtc);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        return await dataSourceProvider.OpenConnectionAsync(cancellationToken);
    }


}
