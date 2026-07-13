using IncidentCompass.Application.Intake.FaultGrouping;
using IncidentCompass.Domain.Incidents;
using IncidentCompass.Infrastructure.Postgres;
using Npgsql;

namespace IncidentCompass.Infrastructure.Intake;

internal sealed class PostgresFaultRepository(PostgresDataSourceProvider dataSourceProvider, PostgresIntakeTransactionContext transactionContext) : IFaultRepository
{
    public Task<Fault?> FindOpenFaultAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        string groupingRuleId,
        int groupingRuleVersion,
        CancellationToken cancellationToken)
    {
        return PostgresOperation.ExecuteAsync(
            "find open fault",
            () => FindByGroupKeyAsync(
                tenantId,
                serviceName,
                environment,
                fingerprint,
                fingerprintVersion,
                "('Queued', 'Analyzing')",
                orderByCreatedDesc: false,
                groupingRuleId,
                groupingRuleVersion,
                cancellationToken));
    }

    public Task<Fault?> FindMostRecentClosedFaultAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        string groupingRuleId,
        int groupingRuleVersion,
        CancellationToken cancellationToken)
    {
        return PostgresOperation.ExecuteAsync(
            "find closed fault",
            () => FindByGroupKeyAsync(
                tenantId,
                serviceName,
                environment,
                fingerprint,
                fingerprintVersion,
                "('Completed', 'Failed', 'InsufficientEvidence')",
                orderByCreatedDesc: true,
                groupingRuleId,
                groupingRuleVersion,
                cancellationToken));
    }

    public async Task<Fault?> TryInsertAsync(Fault fault, CancellationToken cancellationToken)
    {
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO incidentcompass.faults (
                id, trigger_signal_id, tenant_id, status, fingerprint, fingerprint_version, fingerprint_strength,
                service_name, environment, severity, correlation_id, created_at_utc, completed_at_utc, recurrence_of, grouping_rule_id, grouping_rule_version)
            VALUES (
                @id, @trigger_signal_id, @tenant_id, @status, @fingerprint, @fingerprint_version, @fingerprint_strength,
                @service_name, @environment, @severity, @correlation_id, @created_at_utc, @completed_at_utc, @recurrence_of, @grouping_rule_id, @grouping_rule_version)
            ON CONFLICT (tenant_id, service_name, environment, fingerprint, fingerprint_version, grouping_rule_id, grouping_rule_version)
                WHERE status IN ('Queued', 'Analyzing') AND can_group
                DO NOTHING
            RETURNING id;
            """, lease.Connection, lease.Transaction);

        command.AddParameter("id", fault.Id);
        command.AddParameter("trigger_signal_id", fault.TriggerSignalId);
        command.AddParameter("tenant_id", fault.TenantId);
        command.AddParameter("status", fault.Status.ToDbString());
        command.AddParameter("fingerprint", fault.Fingerprint);
        command.AddParameter("fingerprint_version", fault.FingerprintVersion);
        command.AddParameter("fingerprint_strength", fault.FingerprintStrength.ToLowerDbString());
        command.AddParameter("service_name", fault.ServiceName);
        command.AddParameter("environment", fault.Environment);
        command.AddParameter("severity", fault.Severity);
        command.AddParameter("correlation_id", fault.CorrelationId);
        command.AddParameter("created_at_utc", fault.CreatedAtUtc);
        command.AddParameter("completed_at_utc", fault.CompletedAtUtc);
        command.AddParameter("recurrence_of", fault.RecurrenceOf);
        command.AddParameter("grouping_rule_id", fault.GroupingRuleId);
        command.AddParameter("grouping_rule_version", fault.GroupingRuleVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var inserted = await reader.ReadAsync(cancellationToken);
        return inserted ? fault : null;
    }

    public Task<Fault?> FindByIdForUpdateAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!transactionContext.HasCurrent)
        {
            throw new InvalidOperationException("A fault row can only be locked inside an intake unit of work.");
        }

        return FindByIdAsync(id, lockForUpdate: true, cancellationToken);
    }

    public Task<Fault?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return PostgresOperation.ExecuteAsync(
            "find fault",
            () => FindByIdAsync(id, lockForUpdate: false, cancellationToken));
    }

    private async Task<Fault?> FindByIdAsync(
        Guid id,
        bool lockForUpdate,
        CancellationToken cancellationToken)
    {
        var lockingClause = lockForUpdate ? "FOR UPDATE" : string.Empty;
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT {PostgresFaultRowMapper.SelectColumns}
            FROM incidentcompass.faults
            WHERE id = @id
            {lockingClause};
            """, lease.Connection, lease.Transaction);

        command.AddParameter("id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgresFaultRowMapper.Map(reader) : null;
    }

    private async Task<Fault?> FindByGroupKeyAsync(
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        string statuses,
        bool orderByCreatedDesc,
        string groupingRuleId,
        int groupingRuleVersion,
        CancellationToken cancellationToken)
    {
        var orderBy = orderByCreatedDesc ? "ORDER BY created_at_utc DESC" : string.Empty;
        await using var lease = await transactionContext.OpenConnectionAsync(dataSourceProvider, cancellationToken);
        await using var command = new NpgsqlCommand($"""
            SELECT {PostgresFaultRowMapper.SelectColumns}
            FROM incidentcompass.faults
            WHERE tenant_id = @tenant_id
              AND service_name = @service_name
              AND environment = @environment
              AND fingerprint = @fingerprint
              AND fingerprint_version = @fingerprint_version
              AND grouping_rule_id = @grouping_rule_id
              AND grouping_rule_version = @grouping_rule_version
              AND status IN {statuses}
            {orderBy}
            LIMIT 1;
            """, lease.Connection, lease.Transaction);

        AddGroupKeyParameters(command, tenantId, serviceName, environment, fingerprint, fingerprintVersion, groupingRuleId, groupingRuleVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? PostgresFaultRowMapper.Map(reader) : null;
    }

    private static void AddGroupKeyParameters(
        NpgsqlCommand command,
        string tenantId,
        string serviceName,
        string environment,
        string fingerprint,
        int fingerprintVersion,
        string groupingRuleId,
        int groupingRuleVersion)
    {
        command.AddParameter("tenant_id", tenantId);
        command.AddParameter("service_name", serviceName);
        command.AddParameter("environment", environment);
        command.AddParameter("fingerprint", fingerprint);
        command.AddParameter("fingerprint_version", fingerprintVersion);
        command.AddParameter("grouping_rule_id", groupingRuleId);
        command.AddParameter("grouping_rule_version", groupingRuleVersion);
    }

}
