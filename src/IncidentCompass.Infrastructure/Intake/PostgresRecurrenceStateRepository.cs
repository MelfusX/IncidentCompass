using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresRecurrenceStateRepository(
    PostgresDataSourceProvider dataSourceProvider,
    PostgresIntakeTransactionContext transactionContext) : IRecurrenceStateRepository
{
    public async Task<RecurrenceState> RecordAsync(
        RecurrenceOccurrence occurrence,
        CancellationToken cancellationToken)
    {
        if (!transactionContext.HasCurrent)
        {
            throw new InvalidOperationException("Recurrence state must be recorded inside an intake transaction.");
        }

        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.recurrence_states (
                tenant_id, service_name, environment, fingerprint, fingerprint_version, grouping_rule_id,
                grouping_rule_version, recurrence_count, first_recurrence_at_utc, last_recurrence_at_utc,
                escalation_intent_job_id, escalation_intent_fault_id)
            VALUES (
                @tenant_id, @service_name, @environment, @fingerprint, @fingerprint_version, @grouping_rule_id,
                @grouping_rule_version, 1, @occurred_at_utc, @occurred_at_utc,
                CASE WHEN @escalate_after_count > 0 AND 1 >= @escalate_after_count THEN @job_id END,
                CASE WHEN @escalate_after_count > 0 AND 1 >= @escalate_after_count THEN @fault_id END)
            ON CONFLICT (tenant_id, service_name, environment, fingerprint, fingerprint_version, grouping_rule_id, grouping_rule_version)
            DO UPDATE SET
                recurrence_count = incidentcompass.recurrence_states.recurrence_count + 1,
                first_recurrence_at_utc = LEAST(
                    incidentcompass.recurrence_states.first_recurrence_at_utc,
                    EXCLUDED.first_recurrence_at_utc),
                last_recurrence_at_utc = GREATEST(
                    incidentcompass.recurrence_states.last_recurrence_at_utc,
                    EXCLUDED.last_recurrence_at_utc),
                escalation_intent_job_id = CASE
                    WHEN incidentcompass.recurrence_states.escalation_intent_job_id IS NULL
                     AND @escalate_after_count > 0
                     AND incidentcompass.recurrence_states.recurrence_count + 1 >= @escalate_after_count
                    THEN @job_id
                    ELSE incidentcompass.recurrence_states.escalation_intent_job_id
                END,
                escalation_intent_fault_id = CASE
                    WHEN incidentcompass.recurrence_states.escalation_intent_job_id IS NULL
                     AND @escalate_after_count > 0
                     AND incidentcompass.recurrence_states.recurrence_count + 1 >= @escalate_after_count
                    THEN @fault_id
                    ELSE incidentcompass.recurrence_states.escalation_intent_fault_id
                END
            RETURNING recurrence_count, first_recurrence_at_utc, last_recurrence_at_utc,
                      escalation_intent_job_id, escalation_intent_fault_id;
            """, lease.Connection, lease.Transaction);
        command.AddParameter("tenant_id", occurrence.TenantId);
        command.AddParameter("service_name", occurrence.ServiceName);
        command.AddParameter("environment", occurrence.Environment);
        command.AddParameter("fingerprint", occurrence.Fingerprint);
        command.AddParameter("fingerprint_version", occurrence.FingerprintVersion);
        command.AddParameter("grouping_rule_id", occurrence.GroupingRuleId);
        command.AddParameter("grouping_rule_version", occurrence.GroupingRuleVersion);
        command.AddParameter("occurred_at_utc", occurrence.OccurredAtUtc);
        command.AddParameter("escalate_after_count", occurrence.EscalateAfterCount);
        command.AddParameter("job_id", occurrence.JobId);
        command.AddParameter("fault_id", occurrence.FaultId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Recording recurrence state did not return a durable row.");
        }

        return new RecurrenceState(
            reader.GetInt32(0),
            reader.GetDateTimeOffset(1),
            reader.GetDateTimeOffset(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4));
    }
}
